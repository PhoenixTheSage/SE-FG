using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using ClientPlugin.FrameGen;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRageMath;
using Device = SharpDX.Direct3D11.Device;

// Only the game boundary is stubbed; history, failure policy, graphics code,
// math, and baked shaders all come from the production sources.
namespace VRageRender
{
    public static class MyRender11 { public static EnvironmentData Environment = new(); }
    public sealed class EnvironmentData { public MatricesData Matrices = new(); }
    public sealed class MatricesData
    {
        public Matrix ViewProjectionAt0 = Matrix.Identity, InvViewProjectionAt0 = Matrix.Identity;
        public Matrix ViewAt0 = Matrix.Identity;
        public Vector3D CameraPosition;
        public float FovV = 1f;
    }
}
namespace ClientPlugin.FrameGen
{
    internal static class FrameGenHost
    {
        public static string Error;
        public static void SetError(string value) => Error = value;
    }
}

static class RuntimeRegression
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message + ": " + FrameGenHost.Error);
        Console.WriteLine("PASS " + message);
    }

    static Texture2D Texture(Device d, Format format, BindFlags flags, int width = 64, int height = 16) =>
        new(d, new Texture2DDescription { Width = width, Height = height, MipLevels = 1, ArraySize = 1,
            Format = format, BindFlags = flags, Usage = ResourceUsage.Default,
            SampleDescription = new SampleDescription(1, 0) });

    static double Half(ushort h)
    {
        int e = (h >> 10) & 31, m = h & 1023;
        return (e == 0 ? m * Math.Pow(2, -24) : (1 + m / 1024.0) * Math.Pow(2, e - 15)) *
               ((h & 32768) == 0 ? 1 : -1);
    }

    static double Motion(Device d, DeviceContext c, IntPtr pointer, int channel = 0, int x = 32, int y = 8)
    {
        Marshal.AddRef(pointer);
        using var texture = new Texture2D(pointer);
        var desc = texture.Description;
        desc.BindFlags = BindFlags.None; desc.Usage = ResourceUsage.Staging; desc.CpuAccessFlags = CpuAccessFlags.Read;
        using var staging = new Texture2D(d, desc);
        c.CopyResource(texture, staging);
        var mapped = c.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        try { return Half((ushort)Marshal.ReadInt16(mapped.DataPointer, y * mapped.RowPitch + x * 4 + channel * 2)); }
        finally { c.UnmapSubresource(staging, 0); }
    }

    static IntPtr Generate(Device d, DeviceContext c, Texture2D depth)
    {
        var inverse = new float[16]; var current = new float[16]; var previous = new float[16];
        CameraHistory.CopyToArray(CameraHistory.InvViewProjection, inverse);
        CameraHistory.CopyToArray(CameraHistory.ViewProjection, current);
        CameraHistory.CopyToArray(CameraHistory.PreviousViewProjection, previous);
        return FrameGenD3d.GenerateCameraMotionVectors(d, c, depth, 64, 16, inverse, current, previous);
    }

    static void CameraAndState(Device d, DeviceContext c)
    {
        using var depth = Texture(d, Format.R32_Float, BindFlags.ShaderResource | BindFlags.RenderTarget);
        using var depthRtv = new RenderTargetView(d, depth);
        c.ClearRenderTargetView(depthRtv, new RawColor4(.5f, 0, 0, 0));
        var env = VRageRender.MyRender11.Environment.Matrices;
        CameraHistory.Reset(); env.CameraPosition = new Vector3D(1e12, 1e12, 0);
        CameraHistory.BeginFrame(); Check(CameraHistory.ConsumeCameraCut(), "first camera sample seeds history");
        env.CameraPosition += new Vector3D(.25, .25, 0);
        CameraHistory.BeginFrame(); Check(!CameraHistory.ConsumeCameraCut(), "sub-cut translation retains history");

        var bd = BlendStateDescription.Default(); bd.RenderTarget[0].RenderTargetWriteMask = (ColorWriteMaskFlags)0;
        using var blend = new BlendState(d, bd);
        using var raster = new RasterizerState(d, new RasterizerStateDescription {
            FillMode = FillMode.Solid, CullMode = CullMode.None, IsDepthClipEnabled = true, IsScissorEnabled = true });
        using var originalSrv = new ShaderResourceView(d, depth);
        using var originalTarget = Texture(d, Format.R16G16B16A16_Float, BindFlags.RenderTarget);
        using var originalRtv = new RenderTargetView(d, originalTarget);
        c.OutputMerger.BlendState = blend; c.Rasterizer.State = raster;
        c.Rasterizer.SetScissorRectangles(new RawRectangle(0, 0, 1, 1));
        c.PixelShader.SetShaderResource(3, originalSrv);
        c.OutputMerger.SetTargets(originalRtv);
        foreach (var legacy in new[] { false, true })
        {
            if (legacy)
            {
                D3dStateScope.Release();
                typeof(D3dStateScope).GetField("_probed", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
                typeof(D3dStateScope).GetField("_contextPointer", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, c.NativePointer);
            }
            var mv = Generate(d, c, depth);
            Check(mv != IntPtr.Zero && Math.Abs(Motion(d, c, mv) - 8) < .01 &&
                  Math.Abs(Motion(d, c, mv, 1) + 2) < .01,
                  "baked translation direction/large-origin precision and full writes (legacy=" + legacy + ")");
            using var restoredBlend = c.OutputMerger.BlendState;
            using var restoredRaster = c.Rasterizer.State;
            var srvs = c.PixelShader.GetShaderResources(3, 1);
            var rtvs = c.OutputMerger.GetRenderTargets(1);
            try
            {
                Check(restoredBlend.NativePointer == blend.NativePointer && restoredRaster.NativePointer == raster.NativePointer &&
                      srvs[0].NativePointer == originalSrv.NativePointer && rtvs[0].NativePointer == originalRtv.NativePointer,
                      "restore host blend/raster/SRV/RTV state (legacy=" + legacy + ")");
            }
            finally { srvs[0]?.Dispose(); rtvs[0]?.Dispose(); }
        }
        c.ClearState(); D3dStateScope.Release();
        env.CameraPosition += new Vector3D(41, 0, 0);
        CameraHistory.BeginFrame(); Check(CameraHistory.ConsumeCameraCut(), "large translation still resets history");
    }

    static void Recovery()
    {
        var state = new FrameInsertionState();
        Check(state.NeedsReset, "new session seeds color history");
        state.GenerationSucceeded(); state.RequireReset();
        Check(state.NeedsReset, "missed scene requires a fresh seed");
        for (int i = 1; i <= 3; i++)
        {
            state.GenerationSucceeded(); // Includes recovery seeds; none presented yet.
            state.Failed(); // Blit, sprite restore, or Present failed.
            Check(state.ConsecutiveFailures == i && state.NeedsReset, "post-generation failure survives reseeding " + i);
        }
        Check(state.Blocked, "three insertion failures stop retries");
        state.Reset(); state.GenerationSucceeded(); state.Presented();
        Check(!state.Blocked && !state.NeedsReset && state.ConsecutiveFailures == 0, "explicit retry and successful insertion recover");
    }

    static void PerspectiveCamera(Device d, DeviceContext c)
    {
        using var depth = Texture(d, Format.R32_Float, BindFlags.ShaderResource | BindFlags.RenderTarget);
        using var rtv = new RenderTargetView(d, depth);
        c.ClearRenderTargetView(rtv, new RawColor4(.25f, 0, 0, 0));
        var projection = Matrix.CreatePerspectiveFovRhInfiniteComplementary(1.1f, 4f, .1f);
        var current = Matrix.CreateRotationY(.04f) * projection;
        var inverse = Matrix.Invert(current);
        var moved = Matrix.CreateTranslation(.015f, -.01f, -.02f) * Matrix.CreateRotationY(.02f) * projection;
        foreach (var previous in new[] { current, moved })
        {
            var inv = new float[16]; var cur = new float[16]; var prev = new float[16];
            CameraHistory.CopyToArray(inverse, inv); CameraHistory.CopyToArray(current, cur); CameraHistory.CopyToArray(previous, prev);
            var motion = FrameGenD3d.GenerateCameraMotionVectors(d, c, depth, 64, 16, inv, cur, prev);
            bool correct = motion != IntPtr.Zero;
            foreach (var pixel in new[] { new Vector2I(0, 0), new Vector2I(63, 0), new Vector2I(0, 15), new Vector2I(63, 15), new Vector2I(32, 8) })
            {
                var clip = new Vector4((pixel.X + .5f) / 64 * 2 - 1, 1 - (pixel.Y + .5f) / 16 * 2, .25f, 1);
                var world = Vector4.Transform(clip, inverse); world /= world.W;
                var old = Vector4.Transform(world, previous); old /= old.W;
                double expectedX = (old.X - clip.X) * 32, expectedY = (clip.Y - old.Y) * 8;
                correct &= Math.Abs(Motion(d, c, motion, 0, pixel.X, pixel.Y) - expectedX) < .02 &&
                           Math.Abs(Motion(d, c, motion, 1, pixel.X, pixel.Y) - expectedY) < .02;
            }
            Check(correct, previous == current ? "stationary perspective camera has no edge motion" :
                "perspective rotation/translation matches CPU reprojection at center and corners");
        }
    }

    static void Resize(Device d, DeviceContext c)
    {
        foreach (var size in new[] { 64, 96, 64 })
        {
            using var color = Texture(d, Format.R8G8B8A8_UNorm_SRgb,
                BindFlags.RenderTarget | BindFlags.ShaderResource, size, 24);
            using var colorRtv = new RenderTargetView(d, color);
            using var depth = Texture(d, Format.R32_Float, BindFlags.ShaderResource | BindFlags.RenderTarget, size, 24);
            using var depthRtv = new RenderTargetView(d, depth);
            using var mv = Texture(d, Format.R16G16_Float, BindFlags.ShaderResource | BindFlags.RenderTarget, size, 24);
            using var mvRtv = new RenderTargetView(d, mv);
            c.ClearRenderTargetView(colorRtv, new RawColor4(.01f, .02f, .03f, 1));
            c.ClearRenderTargetView(depthRtv, new RawColor4(.5f, 0, 0, 0));
            c.ClearRenderTargetView(mvRtv, new RawColor4(4, 0, 0, 0));
            Check(FrameGenD3d.EnsureColorCopies(d, color.Description) && FrameGenD3d.CopyOrStretchToScene(d, c, color),
                "resize allocates/captures color copies " + size);
            Check(FrameGenD3d.Interpolate(d, c, FrameGenD3d.SceneCopy, depth, mv.NativePointer,
                FrameGenD3d.InterpCopy, (uint)size, 24, 0) == 1, "resize invalidates old interpolation history " + size);
            FrameGenD3d.CopyFromBackbuffer(c, color, FrameGenD3d.HudCopy);
            FrameGenD3d.CopyFromBackbuffer(c, color, FrameGenD3d.CurrCopy);
            Check(FrameGenD3d.Interpolate(d, c, FrameGenD3d.SceneCopy, depth, mv.NativePointer,
                FrameGenD3d.InterpCopy, (uint)size, 24, 0) == 0 &&
                FrameGenD3d.BlitGeneratedFrame(d, c, color) && FrameGenD3d.RestoreSpritesAfterHud(d, c, color, true),
                "resized cached views and replacement external velocity composite successfully " + size);
        }
    }

    static void Discovery()
    {
        var retry = new DiscoveryRetry();
        Check(retry.ShouldRetry() && !retry.ShouldRetry(), "negative optional discovery is cached");
        AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("FrameGenLateLoadRegression"), AssemblyBuilderAccess.Run);
        Check(retry.ShouldRetry(), "late assembly load retries optional discovery immediately");
        retry.Reset(); Check(retry.ShouldRetry(), "discovery reset retries immediately");
    }

    static void DepthHistory(Device d, DeviceContext c, bool legacy = false)
    {
        FrameGenD3d.Release();
        if (legacy)
        {
            typeof(D3dStateScope).GetField("_probed", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
            typeof(D3dStateScope).GetField("_contextPointer", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, c.NativePointer);
        }
        using var color = Texture(d, Format.R16G16B16A16_Float, BindFlags.ShaderResource | BindFlags.RenderTarget);
        using var colorRtv = new RenderTargetView(d, color);
        using var mv = Texture(d, Format.R16G16_Float, BindFlags.ShaderResource | BindFlags.RenderTarget);
        using var mvRtv = new RenderTargetView(d, mv);
        using var hostSrv = new ShaderResourceView(d, color);
        c.ClearRenderTargetView(mvRtv, new RawColor4(4, 0, 0, 0));
        Check(FrameGenD3d.EnsureColorCopies(d, color.Description), "depth history keeps output allocation");
        foreach (var format in legacy ? new[] { Format.R32G8X24_Typeless } :
            new[] { Format.R32G8X24_Typeless, Format.R24G8_Typeless, Format.R32_Typeless, Format.R16_Typeless })
        foreach (var size in legacy ? new[] { 64 } : new[] { 64, 32, 64 })
        {
            var viewFormat = format == Format.R32G8X24_Typeless ? Format.D32_Float_S8X24_UInt :
                format == Format.R24G8_Typeless ? Format.D24_UNorm_S8_UInt :
                format == Format.R32_Typeless ? Format.D32_Float : Format.D16_UNorm;
            using var depth = Texture(d, format, BindFlags.ShaderResource | BindFlags.DepthStencil, size, 16);
            using var dsv = new DepthStencilView(d, depth, new DepthStencilViewDescription {
                Format = viewFormat, Dimension = DepthStencilViewDimension.Texture2D });
            c.ClearRenderTargetView(colorRtv, new RawColor4(.6f, .6f, .6f, 1));
            c.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, .5f, 0);
            // No explicit reset: format/internal depth size changes must seed
            // both histories even though the output color size is unchanged.
            Check(FrameGenD3d.Interpolate(d, c, color, depth, mv.NativePointer,
                FrameGenD3d.InterpCopy, 64, 16, 0) == 1, "native depth change reseeds " + format + "/" + size);
            c.ClearRenderTargetView(colorRtv, new RawColor4(.01f, .01f, .01f, 1));
            c.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 0f, 0);
            c.ComputeShader.SetShaderResource(5, hostSrv);
            Check(FrameGenD3d.Interpolate(d, c, color, depth, mv.NativePointer,
                FrameGenD3d.InterpCopy, 64, 16, 0) == 0, "native depth history dispatch " + format + "/" + size);
            var saved = c.ComputeShader.GetShaderResources(5, 1);
            try { Check(saved[0]?.NativePointer == hostSrv.NativePointer, "history SRV slot restores host binding " + format + "/" + size); }
            finally { saved[0]?.Dispose(); }
            c.ComputeShader.SetShaderResource(5, null);

            var desc = FrameGenD3d.InterpCopy.Description;
            desc.BindFlags = BindFlags.None; desc.Usage = ResourceUsage.Staging; desc.CpuAccessFlags = CpuAccessFlags.Read;
            using var staging = new Texture2D(d, desc);
            c.CopyResource(FrameGenD3d.InterpCopy, staging);
            var box = c.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            try { Check(Math.Abs(Half((ushort)Marshal.ReadInt16(box.DataPointer, 8 * box.RowPitch + 32 * 8)) - .01) < .0001,
                "native previous ship coverage rejects sky ghost " + format + "/" + size); }
            finally { c.UnmapSubresource(staging, 0); }
        }
        Check(FrameGenD3d.Interpolate(d, c, color, null, mv.NativePointer,
            FrameGenD3d.InterpCopy, 64, 16, 0) == 1, "missing depth invalidates paired history");
        using var restoredDepth = Texture(d, Format.R32_Typeless, BindFlags.ShaderResource | BindFlags.DepthStencil);
        Check(FrameGenD3d.Interpolate(d, c, color, restoredDepth, mv.NativePointer,
            FrameGenD3d.InterpCopy, 64, 16, 0) == 1, "restored depth seeds before interpolation");
    }

    static void Main()
    {
        Recovery(); Discovery();
        using var device = new Device(DriverType.Warp, DeviceCreationFlags.None);
        using var context = device.ImmediateContext;
        try { CameraAndState(device, context); PerspectiveCamera(device, context); Resize(device, context);
            DepthHistory(device, context); DepthHistory(device, context, legacy: true); }
        finally { context.ClearState(); FrameGenD3d.Release(); }
    }
}
