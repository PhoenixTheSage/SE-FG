using System;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;

namespace ClientPlugin.FrameGen;

internal static class FrameGenD3d
{
    private const int MvConstantBufferSize = 208;
    private const int InterpolateConstantBufferSize = 32;

    private static Texture2D _mvTex;
    private static RenderTargetView _mvRtv;
    private static ShaderResourceView _mvSrv;
    private static VertexShader _mvVs;
    private static PixelShader _mvPs;
    private static Buffer _mvCb;
    private static SamplerState _mvSampler;
    private static uint _mvW;
    private static uint _mvH;

    private static Texture2D _currCopy;
    private static Texture2D _interpCopy;
    private static uint _copyW;
    private static uint _copyH;
    private static Format _copyFmt = Format.Unknown;

    private static ComputeShader _cs;
    private static Buffer _csCb;
    private static SamplerState _csSampler;
    private static Texture2D _prevColor;
    private static ShaderResourceView _prevSrv;
    private static Texture2D _uavTex;
    private static UnorderedAccessView _uav;
    private static uint _fgW;
    private static uint _fgH;
    private static Format _fgFmt = Format.Unknown;
    private static bool _hasPrev;

    private static IntPtr _cachedDepthRes;
    private static ShaderResourceView _cachedDepthSrv;
    private static readonly ShaderResourceView[] NullSrvs = new ShaderResourceView[8];
    private static readonly UnorderedAccessView[] NullUavs = new UnorderedAccessView[8];
    private static readonly byte[] MvCbScratch = new byte[MvConstantBufferSize];
    private static readonly byte[] CsCbScratch = new byte[InterpolateConstantBufferSize];

    internal static void Release()
    {
        ReleaseMvPipeline();
        ReleaseCopies();
        ReleaseMvShaders();
        ReleaseInterpolator();
        DisposeView(ref _cachedDepthSrv);
        _cachedDepthRes = IntPtr.Zero;
    }

    internal static bool EnsureInterpolator(Device device, uint width, uint height)
    {
        return device != null && !device.IsDisposed &&
               EnsureInterpolateShaders(device) &&
               width > 0 && height > 0;
    }

    internal static IntPtr GenerateCameraMotionVectors(
        Device device,
        DeviceContext context,
        Resource depth,
        uint width,
        uint height,
        float[] invViewProj,
        float[] unjitteredViewProj,
        float[] prevViewProj)
    {
        if (device == null || context == null || depth == null || width == 0 || height == 0)
        {
            FrameGenHost.SetError("motion-vector args are incomplete");
            return IntPtr.Zero;
        }

        if (!EnsureMvShaders(device) || !EnsureMvTarget(device, width, height))
            return IntPtr.Zero;

        FillMvConstantBuffer(width, height, invViewProj, unjitteredViewProj, prevViewProj);
        try
        {
            var mapped = context.MapSubresource(_mvCb, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
            Marshal.Copy(MvCbScratch, 0, mapped.DataPointer, MvConstantBufferSize);
            context.UnmapSubresource(_mvCb, 0);
        }
        catch (Exception e)
        {
            FrameGenHost.SetError("failed to map motion-vector constant buffer: " + e.GetType().Name);
            return IntPtr.Zero;
        }

        context.OutputMerger.SetTargets((DepthStencilView)null, (RenderTargetView)null);

        if (!TryGetTexture2D(depth, out var depthTex))
        {
            FrameGenHost.SetError("motion-vector depth is not a Texture2D");
            return IntPtr.Zero;
        }

        if (!EnsureDepthSrv(device, depth, depthTex.Description))
        {
            FrameGenHost.SetError("failed to create depth SRV for motion vectors");
            return IntPtr.Zero;
        }

        context.Rasterizer.SetViewport(0, 0, width, height, 0f, 1f);
        context.InputAssembler.InputLayout = null;
        context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
        context.VertexShader.Set(_mvVs);
        context.VertexShader.SetConstantBuffer(0, _mvCb);
        context.PixelShader.SetConstantBuffer(0, _mvCb);
        context.PixelShader.SetSampler(0, _mvSampler);
        context.OutputMerger.SetTargets(_mvRtv);
        context.PixelShader.Set(_mvPs);
        context.PixelShader.SetShaderResource(0, _cachedDepthSrv);
        context.Draw(3, 0);
        context.PixelShader.SetShaderResource(0, null);
        context.OutputMerger.SetTargets((RenderTargetView)null);
        return _mvTex.NativePointer;
    }

    internal static IntPtr EnsureMotionOrZero(Device device, DeviceContext context, IntPtr motion, uint width, uint height)
    {
        if (motion != IntPtr.Zero)
            return motion;
        if (device == null || !EnsureMvTarget(device, width, height))
            return IntPtr.Zero;
        context.ClearRenderTargetView(_mvRtv, new RawColor4(0, 0, 0, 0));
        return _mvTex.NativePointer;
    }

    internal static bool EnsureColorCopies(Device device, Texture2DDescription desc)
    {
        if (_currCopy != null && _interpCopy != null &&
            _copyW == (uint)desc.Width && _copyH == (uint)desc.Height && _copyFmt == desc.Format)
            return true;
        ReleaseCopies();
        try
        {
            var td = new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget
            };
            _currCopy = new Texture2D(device, td);
            _interpCopy = new Texture2D(device, td);
            _copyW = (uint)desc.Width;
            _copyH = (uint)desc.Height;
            _copyFmt = desc.Format;
            return true;
        }
        catch (Exception e)
        {
            ReleaseCopies();
            FrameGenHost.SetError("failed to create color copies: " + e.GetType().Name);
            return false;
        }
    }

    internal static Texture2D CurrCopy => _currCopy;
    internal static Texture2D InterpCopy => _interpCopy;

    internal static int Interpolate(
        Device device,
        DeviceContext context,
        Resource color,
        Resource depth,
        IntPtr motionVectors,
        Resource output,
        uint width,
        uint height,
        int reset)
    {
        if (device == null || context == null || color == null || output == null || width == 0 || height == 0)
        {
            FrameGenHost.SetError("Interpolate missing device, color, or output");
            return -1;
        }

        if (!TryGetTexture2D(color, out var colorTex))
        {
            FrameGenHost.SetError("color is not a Texture2D");
            return -1;
        }

        if (!EnsureInterpolateShaders(device) ||
            !EnsureInterpolatorTargets(device, width, height, colorTex.Description.Format))
            return -2;

        if (reset != 0 || !_hasPrev || depth == null || motionVectors == IntPtr.Zero)
        {
            context.CopyResource(color, _prevColor);
            context.CopyResource(color, output);
            _hasPrev = true;
            FrameGenHost.SetError("skipped");
            return 1;
        }

        if (!TryGetTexture2D(depth, out var depthTex) ||
            !EnsureDepthSrv(device, depth, depthTex.Description))
        {
            FrameGenHost.SetError("failed to create depth SRV");
            return -5;
        }

        ShaderResourceView currSrv = null;
        ShaderResourceView mvSrv = null;
        Texture2D mvWrap = null;
        try
        {
            if (!TryCreateColorSrv(device, color, colorTex.Description.Format, out currSrv))
            {
                FrameGenHost.SetError("failed to create color SRV");
                return -5;
            }

            if (!TryGetMotionSrv(device, motionVectors, out mvSrv, out mvWrap))
            {
                FrameGenHost.SetError("failed to create motion SRV");
                return -5;
            }

            FillInterpolateConstantBuffer(width, height, 1f, 1f, invertedDepth: 1);
            var mapped = context.MapSubresource(_csCb, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
            Marshal.Copy(CsCbScratch, 0, mapped.DataPointer, InterpolateConstantBufferSize);
            context.UnmapSubresource(_csCb, 0);

            UnbindPipeline(context);
            context.ComputeShader.Set(_cs);
            context.ComputeShader.SetConstantBuffer(0, _csCb);
            context.ComputeShader.SetSampler(0, _csSampler);
            context.ComputeShader.SetShaderResource(0, _prevSrv);
            context.ComputeShader.SetShaderResource(1, currSrv);
            context.ComputeShader.SetShaderResource(2, _cachedDepthSrv);
            context.ComputeShader.SetShaderResource(3, mvSrv);
            context.ComputeShader.SetUnorderedAccessView(0, _uav);
            context.Dispatch((int)((width + 7) / 8), (int)((height + 7) / 8), 1);
            UnbindPipeline(context);

            context.CopyResource(_uavTex, output);
            context.CopyResource(color, _prevColor);
            _hasPrev = true;
            FrameGenHost.SetError("ok");
            return 0;
        }
        catch (Exception e)
        {
            FrameGenHost.SetError("Interpolate threw: " + e.GetType().Name + ": " + e.Message);
            return -1;
        }
        finally
        {
            DisposeView(ref currSrv);
            if (mvWrap != null)
                DisposeView(ref mvSrv);
            DisposeView(ref mvWrap);
        }
    }

    internal static void UnbindPipeline(DeviceContext context)
    {
        context.OutputMerger.ResetTargets();
        context.PixelShader.SetShaderResources(0, NullSrvs);
        context.VertexShader.SetShaderResources(0, NullSrvs);
        try
        {
            context.ComputeShader.SetShaderResources(0, NullSrvs);
            context.ComputeShader.SetUnorderedAccessViews(0, NullUavs);
            context.OutputMerger.SetUnorderedAccessViews(0, NullUavs);
            context.ComputeShader.Set(null);
        }
        catch
        {
            // Feature level or SharpDX build without CS UAV helpers.
        }
    }

    internal static bool ValidateVelocity(Device device, IntPtr pointer, int width, int height, out string evidence,
        Resource known = null)
    {
        evidence = "invalid velocity texture";
        if (pointer == IntPtr.Zero || device == null)
            return false;
        try
        {
            if (known is Texture2D live && !live.IsDisposed &&
                (known.NativePointer == IntPtr.Zero || known.NativePointer == pointer))
                return AcceptVelocity(device, pointer, live, width, height, out evidence);

            var iid = typeof(Texture2D).GUID;
            IntPtr queried = IntPtr.Zero;
            try
            {
#if NETFRAMEWORK
                if (Marshal.QueryInterface(pointer, ref iid, out queried) != 0)
#else
                if (Marshal.QueryInterface(pointer, in iid, out queried) != 0)
#endif
                    return false;
                using (var texture = new Texture2D(queried))
                {
                    queried = IntPtr.Zero;
                    return AcceptVelocity(device, pointer, texture, width, height, out evidence);
                }
            }
            finally
            {
                if (queried != IntPtr.Zero)
                    Marshal.Release(queried);
            }
        }
        catch (Exception e)
        {
            evidence = "velocity texture unreadable: " + e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    private static bool AcceptVelocity(Device device, IntPtr pointer, Texture2D texture, int width, int height,
        out string evidence)
    {
        var desc = texture.Description;
        evidence = "0x" + pointer.ToInt64().ToString("X") + " " + desc.Width + "x" + desc.Height + " " + desc.Format;
        var owner = texture.Device;
        if (owner == null || owner.IsDisposed || owner.NativePointer != device.NativePointer)
        {
            evidence += " wrong-device";
            return false;
        }
        if (desc.Width != width || desc.Height != height)
        {
            evidence += " size-mismatch";
            return false;
        }
        if (desc.Format != Format.R16G16_Float || desc.SampleDescription.Count != 1 ||
            (desc.BindFlags & BindFlags.ShaderResource) == 0)
        {
            evidence += " format-mismatch";
            return false;
        }
        return true;
    }

    private static bool EnsureMvShaders(Device device)
    {
        if (_mvVs != null && _mvPs != null && _mvCb != null && _mvSampler != null)
            return true;
        try
        {
            _mvVs = new VertexShader(device, ShaderBytecode.FullscreenVs);
            _mvPs = new PixelShader(device, ShaderBytecode.MvPs);
            _mvCb = new Buffer(device, new BufferDescription
            {
                SizeInBytes = MvConstantBufferSize,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write
            });
            _mvSampler = new SamplerState(device, new SamplerStateDescription
            {
                Filter = Filter.MinMagMipPoint,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });
            return true;
        }
        catch (Exception e)
        {
            ReleaseMvShaders();
            FrameGenHost.SetError("failed to create motion-vector pipeline: " + e.GetType().Name);
            return false;
        }
    }

    private static bool EnsureInterpolateShaders(Device device)
    {
        if (_cs != null && _csCb != null && _csSampler != null)
            return true;
        try
        {
            _cs = new ComputeShader(device, ShaderBytecode.InterpolateCs);
            _csCb = new Buffer(device, new BufferDescription
            {
                SizeInBytes = InterpolateConstantBufferSize,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write
            });
            _csSampler = new SamplerState(device, new SamplerStateDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaximumLod = float.MaxValue
            });
            return true;
        }
        catch (Exception e)
        {
            ReleaseInterpolatorShaders();
            FrameGenHost.SetError("failed to create interpolate compute shader: " + e.GetType().Name);
            return false;
        }
    }

    private static bool EnsureInterpolatorTargets(Device device, uint width, uint height, Format colorFormat)
    {
        if (_prevColor != null && _uavTex != null && _fgW == width && _fgH == height && _fgFmt == colorFormat)
            return true;
        ReleaseInterpolatorTargets();
        try
        {
            var prevDesc = new Texture2DDescription
            {
                Width = (int)width,
                Height = (int)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = colorFormat,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget
            };
            _prevColor = new Texture2D(device, prevDesc);
            if (!TryCreateColorSrv(device, _prevColor, colorFormat, out _prevSrv))
            {
                FrameGenHost.SetError("failed to create previous-color SRV");
                ReleaseInterpolatorTargets();
                return false;
            }

            var uavFmt = TypelessColor(colorFormat);
            var uavDesc = new Texture2DDescription
            {
                Width = (int)width,
                Height = (int)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = uavFmt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess | BindFlags.RenderTarget
            };
            try
            {
                _uavTex = new Texture2D(device, uavDesc);
            }
            catch
            {
                uavDesc.Format = Format.R8G8B8A8_UNorm;
                _uavTex = new Texture2D(device, uavDesc);
            }
            _uav = new UnorderedAccessView(device, _uavTex, new UnorderedAccessViewDescription
            {
                Format = uavDesc.Format,
                Dimension = UnorderedAccessViewDimension.Texture2D
            });
            _fgW = width;
            _fgH = height;
            _fgFmt = colorFormat;
            _hasPrev = false;
            return true;
        }
        catch (Exception e)
        {
            ReleaseInterpolatorTargets();
            FrameGenHost.SetError("failed to create interpolate targets: " + e.GetType().Name);
            return false;
        }
    }

    private static bool EnsureMvTarget(Device device, uint width, uint height)
    {
        if (_mvTex != null && _mvW == width && _mvH == height)
            return true;
        ReleaseMvPipeline();
        try
        {
            _mvTex = new Texture2D(device, new Texture2DDescription
            {
                Width = (int)width,
                Height = (int)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R16G16_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            });
            _mvRtv = new RenderTargetView(device, _mvTex);
            _mvSrv = new ShaderResourceView(device, _mvTex);
            _mvW = width;
            _mvH = height;
            return true;
        }
        catch
        {
            ReleaseMvPipeline();
            FrameGenHost.SetError("failed to create motion-vector targets");
            return false;
        }
    }

    private static bool EnsureDepthSrv(Device device, Resource resource, Texture2DDescription desc)
    {
        if (_cachedDepthSrv != null && _cachedDepthRes == resource.NativePointer)
            return true;
        DisposeView(ref _cachedDepthSrv);
        _cachedDepthRes = resource.NativePointer;
        var formats = new[]
        {
            DepthSrvFormat(desc.Format),
            Format.R32_Float_X8X24_Typeless,
            Format.R32_Float,
            Format.R24_UNorm_X8_Typeless
        };
        foreach (var fmt in formats)
        {
            if (fmt == Format.Unknown)
                continue;
            try
            {
                _cachedDepthSrv = new ShaderResourceView(device, resource, new ShaderResourceViewDescription
                {
                    Format = fmt,
                    Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D = { MipLevels = 1 }
                });
                return true;
            }
            catch
            {
                _cachedDepthSrv = null;
            }
        }
        return false;
    }

    private static bool TryCreateColorSrv(Device device, Resource resource, Format resourceFormat,
        out ShaderResourceView srv)
    {
        srv = null;
        var formats = new[]
        {
            TypelessColor(resourceFormat),
            resourceFormat,
            Format.R8G8B8A8_UNorm,
            Format.B8G8R8A8_UNorm,
            Format.R16G16B16A16_Float
        };
        foreach (var fmt in formats)
        {
            try
            {
                srv = new ShaderResourceView(device, resource, new ShaderResourceViewDescription
                {
                    Format = fmt,
                    Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D = { MipLevels = 1 }
                });
                return true;
            }
            catch
            {
                srv = null;
            }
        }
        return false;
    }

    private static bool TryGetMotionSrv(Device device, IntPtr motion, out ShaderResourceView srv, out Texture2D wrap)
    {
        srv = null;
        wrap = null;
        if (motion == IntPtr.Zero)
            return false;
        if (_mvTex != null && _mvSrv != null && _mvTex.NativePointer == motion)
        {
            srv = _mvSrv;
            return true;
        }

        var iid = typeof(Texture2D).GUID;
        IntPtr queried = IntPtr.Zero;
        try
        {
#if NETFRAMEWORK
            if (Marshal.QueryInterface(motion, ref iid, out queried) != 0)
#else
            if (Marshal.QueryInterface(motion, in iid, out queried) != 0)
#endif
                return false;
            wrap = new Texture2D(queried);
            queried = IntPtr.Zero;
            srv = new ShaderResourceView(device, wrap, new ShaderResourceViewDescription
            {
                Format = Format.R16G16_Float,
                Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                Texture2D = { MipLevels = 1 }
            });
            return true;
        }
        catch
        {
            if (queried != IntPtr.Zero)
                Marshal.Release(queried);
            DisposeView(ref srv);
            DisposeView(ref wrap);
            return false;
        }
    }

    private static void FillMvConstantBuffer(
        uint width, uint height, float[] invViewProj, float[] unjitteredViewProj, float[] prevViewProj)
    {
        System.Buffer.BlockCopy(invViewProj, 0, MvCbScratch, 0, 64);
        System.Buffer.BlockCopy(unjitteredViewProj, 0, MvCbScratch, 64, 64);
        System.Buffer.BlockCopy(prevViewProj, 0, MvCbScratch, 128, 64);
        System.Buffer.BlockCopy(BitConverter.GetBytes((float)width), 0, MvCbScratch, 192, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes((float)height), 0, MvCbScratch, 196, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(1f / width), 0, MvCbScratch, 200, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(1f / height), 0, MvCbScratch, 204, 4);
    }

    private static void FillInterpolateConstantBuffer(
        uint width, uint height, float mvScaleX, float mvScaleY, uint invertedDepth)
    {
        System.Buffer.BlockCopy(BitConverter.GetBytes(width), 0, CsCbScratch, 0, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(height), 0, CsCbScratch, 4, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(1f / width), 0, CsCbScratch, 8, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(1f / height), 0, CsCbScratch, 12, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(mvScaleX), 0, CsCbScratch, 16, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(mvScaleY), 0, CsCbScratch, 20, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(invertedDepth), 0, CsCbScratch, 24, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, CsCbScratch, 28, 4);
    }

    private static bool TryGetTexture2D(Resource resource, out Texture2D tex)
    {
        tex = resource as Texture2D;
        return tex != null && !tex.IsDisposed;
    }

    private static Format TypelessColor(Format format)
    {
        switch (format)
        {
            case Format.R8G8B8A8_UNorm_SRgb:
            case Format.R8G8B8A8_UNorm:
            case Format.R8G8B8A8_Typeless:
                return Format.R8G8B8A8_UNorm;
            case Format.B8G8R8A8_UNorm_SRgb:
            case Format.B8G8R8A8_UNorm:
            case Format.B8G8R8A8_Typeless:
                return Format.B8G8R8A8_UNorm;
            case Format.R16G16B16A16_Float:
            case Format.R16G16B16A16_UNorm:
            case Format.R16G16B16A16_Typeless:
                return Format.R16G16B16A16_Float;
            default:
                return format;
        }
    }

    private static Format DepthSrvFormat(Format resourceFormat)
    {
        switch (resourceFormat)
        {
            case Format.R32G8X24_Typeless:
            case Format.D32_Float_S8X24_UInt:
                return Format.R32_Float_X8X24_Typeless;
            case Format.R24G8_Typeless:
            case Format.D24_UNorm_S8_UInt:
                return Format.R24_UNorm_X8_Typeless;
            case Format.R32_Typeless:
            case Format.D32_Float:
                return Format.R32_Float;
            case Format.R16_Typeless:
            case Format.D16_UNorm:
                return Format.R16_UNorm;
            default:
                return resourceFormat;
        }
    }

    private static void ReleaseMvPipeline()
    {
        DisposeView(ref _mvSrv);
        DisposeView(ref _mvRtv);
        DisposeView(ref _mvTex);
        _mvW = _mvH = 0;
    }

    private static void ReleaseCopies()
    {
        DisposeView(ref _currCopy);
        DisposeView(ref _interpCopy);
        _copyW = _copyH = 0;
        _copyFmt = Format.Unknown;
    }

    private static void ReleaseMvShaders()
    {
        DisposeView(ref _mvVs);
        DisposeView(ref _mvPs);
        DisposeView(ref _mvCb);
        DisposeView(ref _mvSampler);
    }

    private static void ReleaseInterpolator()
    {
        ReleaseInterpolatorTargets();
        ReleaseInterpolatorShaders();
    }

    private static void ReleaseInterpolatorTargets()
    {
        DisposeView(ref _uav);
        DisposeView(ref _uavTex);
        DisposeView(ref _prevSrv);
        DisposeView(ref _prevColor);
        _fgW = _fgH = 0;
        _fgFmt = Format.Unknown;
        _hasPrev = false;
    }

    private static void ReleaseInterpolatorShaders()
    {
        DisposeView(ref _cs);
        DisposeView(ref _csCb);
        DisposeView(ref _csSampler);
    }

    private static void DisposeView<T>(ref T view) where T : class, IDisposable
    {
        if (view == null)
            return;
        try { view.Dispose(); }
        catch { /* device tearing down */ }
        view = null;
    }
}
