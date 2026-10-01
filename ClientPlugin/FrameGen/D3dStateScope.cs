using System;
using System.Reflection;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace ClientPlugin.FrameGen;

/// <summary>
/// Keep raw D3D work separate from Keen's cached context state. Context-state
/// swaps preserve the complete pipeline; older D3D11 runtimes save the slots
/// touched by these passes explicitly instead.
/// </summary>
internal sealed class D3dStateScope : IDisposable
{
    private static DeviceContext1 _context1;
    private static DeviceContextState _privateState;
    private static IntPtr _contextPointer;
    private static bool _probed;
    private readonly DeviceContext _context;
    private DeviceContextState _previousState;
    private LegacyState _legacy;

    internal D3dStateScope(DeviceContext context, bool forceLegacy = false)
    {
        _context = context;
        if (!forceLegacy)
        {
            if (_contextPointer != context.NativePointer)
                Release();
            if (!_probed)
            {
                _probed = true;
                _contextPointer = context.NativePointer;
                try
                {
                    _context1 = context.QueryInterface<DeviceContext1>();
                    // DeviceChild.Device is cached/owned by the SharpDX context.
                    using var device1 = context.Device.QueryInterface<Device1>();
                    var flags = (context.Device.CreationFlags & DeviceCreationFlags.SingleThreaded) != 0
                        ? CreateDeviceContextStateFlags.SingleThreaded : (CreateDeviceContextStateFlags)0;
                    _privateState = device1.CreateDeviceContextState<Device>(flags,
                        new[] { context.Device.FeatureLevel }, out _);
                }
                catch
                {
                    _privateState?.Dispose();
                    _privateState = null;
                    _context1?.Dispose();
                    _context1 = null;
                }
            }
            if (_privateState != null)
            {
                _context1.SwapDeviceContextState(_privateState, out _previousState);
                return;
            }
        }
        _legacy = new LegacyState(context);
    }

    public void Dispose()
    {
        if (_previousState != null)
        {
            try
            {
                // Only our private state is active here. Release its bindings,
                // then restore the original state exactly, including Keen's cache.
                _context.ClearState();
                _context1.SwapDeviceContextState(_previousState, out var generatedState);
                generatedState?.Dispose();
            }
            finally
            {
                _previousState.Dispose();
                _previousState = null;
            }
        }
        _legacy?.Dispose();
        _legacy = null;
    }

    internal static void Release()
    {
        _privateState?.Dispose();
        _privateState = null;
        _context1?.Dispose();
        _context1 = null;
        _contextPointer = IntPtr.Zero;
        _probed = false;
    }

    private sealed class LegacyState : IDisposable
    {
        private delegate void GetOmState(OutputMergerStage stage, int count, RenderTargetView[] rtvs,
            out DepthStencilView dsv, int startSlot, int uavCount, UnorderedAccessView[] uavs);
        // SharpDX's public UAV getter unconditionally disposes a null DSV.
        // Use its generated getter directly and own every returned reference.
        private static readonly GetOmState ReadOmState = (GetOmState)Delegate.CreateDelegate(typeof(GetOmState),
            typeof(OutputMergerStage).GetMethod("GetRenderTargetsAndUnorderedAccessViews",
                BindingFlags.Instance | BindingFlags.NonPublic));
        private readonly DeviceContext c;
        private readonly BlendState blend;
        private readonly RawColor4 blendFactor;
        private readonly int sampleMask;
        private readonly DepthStencilState depth;
        private readonly int stencilRef;
        private readonly RasterizerState raster;
        private readonly RawViewportF[] viewports;
        private readonly InputLayout layout;
        private readonly PrimitiveTopology topology;
        private readonly VertexShader vs;
        private readonly PixelShader ps;
        private readonly GeometryShader gs;
        private readonly HullShader hs;
        private readonly DomainShader ds;
        private readonly ComputeShader cs;
        private readonly Buffer[] vsCb, psCb, csCb;
        private readonly SamplerState[] psSamplers, csSamplers;
        private readonly ShaderResourceView[] vsSrvs, psSrvs, csSrvs;
        private readonly UnorderedAccessView[] csUavs;
        private readonly RenderTargetView[] rtvs;
        private readonly DepthStencilView dsv;
        private readonly UnorderedAccessView[] omUavs;
        private readonly Predicate predicate;
        private readonly SharpDX.Mathematics.Interop.RawBool predicateValue;

        internal LegacyState(DeviceContext context)
        {
            c = context;
            blend = c.OutputMerger.GetBlendState(out blendFactor, out sampleMask);
            depth = c.OutputMerger.GetDepthStencilState(out stencilRef);
            raster = c.Rasterizer.State;
            // SharpDX's generic getter indexes element zero even when the
            // native context reports no viewports (e.g. after ClearState).
            try { viewports = c.Rasterizer.GetViewports<RawViewportF>(); }
            catch (IndexOutOfRangeException) { viewports = Array.Empty<RawViewportF>(); }
            layout = c.InputAssembler.InputLayout;
            topology = c.InputAssembler.PrimitiveTopology;
            vs = c.VertexShader.Get(); ps = c.PixelShader.Get();
            gs = c.GeometryShader.Get(); hs = c.HullShader.Get(); ds = c.DomainShader.Get();
            cs = c.ComputeShader.Get();
            vsCb = c.VertexShader.GetConstantBuffers(0, 1);
            psCb = c.PixelShader.GetConstantBuffers(0, 1);
            csCb = c.ComputeShader.GetConstantBuffers(0, 1);
            psSamplers = c.PixelShader.GetSamplers(0, 1);
            csSamplers = c.ComputeShader.GetSamplers(0, 1);
            vsSrvs = c.VertexShader.GetShaderResources(0, 8);
            psSrvs = c.PixelShader.GetShaderResources(0, 8);
            csSrvs = c.ComputeShader.GetShaderResources(0, 8);
            csUavs = c.ComputeShader.GetUnorderedAccessViews(0, 8);
            rtvs = new RenderTargetView[8];
            omUavs = new UnorderedAccessView[8];
            ReadOmState(c.OutputMerger, 8, rtvs, out dsv, 0, 8, omUavs);
            predicate = c.GetPredication(out predicateValue);
        }

        public void Dispose()
        {
            try
            {
                FrameGenD3d.UnbindPipeline(c);
                // RTVs and UAVs share OM slots. Restore UAVs only after the RTV range.
                var rtvCount = rtvs.Length;
                while (rtvCount > 0 && rtvs[rtvCount - 1] == null) rtvCount--;
                var restoredRtvs = new RenderTargetView[rtvCount];
                Array.Copy(rtvs, restoredRtvs, rtvCount);
                c.OutputMerger.SetTargets(dsv, restoredRtvs);
                for (var i = rtvCount; i < omUavs.Length; i++)
                    if (omUavs[i] != null)
                        c.OutputMerger.SetUnorderedAccessView(i, omUavs[i]);
                c.ComputeShader.SetUnorderedAccessViews(0, csUavs);
                c.OutputMerger.SetBlendState(blend, blendFactor, sampleMask);
                c.OutputMerger.SetDepthStencilState(depth, stencilRef);
                c.Rasterizer.State = raster;
                c.Rasterizer.SetViewports(viewports.Length == 0 ? null : viewports);
                c.InputAssembler.InputLayout = layout;
                c.InputAssembler.PrimitiveTopology = topology;
                c.VertexShader.Set(vs); c.PixelShader.Set(ps); c.ComputeShader.Set(cs);
                c.GeometryShader.Set(gs); c.HullShader.Set(hs); c.DomainShader.Set(ds);
                c.VertexShader.SetConstantBuffers(0, vsCb);
                c.PixelShader.SetConstantBuffers(0, psCb);
                c.ComputeShader.SetConstantBuffers(0, csCb);
                c.PixelShader.SetSamplers(0, psSamplers);
                c.ComputeShader.SetSamplers(0, csSamplers);
                c.VertexShader.SetShaderResources(0, vsSrvs);
                c.PixelShader.SetShaderResources(0, psSrvs);
                c.ComputeShader.SetShaderResources(0, csSrvs);
                c.SetPredication(predicate, predicateValue);
            }
            finally
            {
                blend?.Dispose(); depth?.Dispose(); raster?.Dispose(); layout?.Dispose();
                vs?.Dispose(); ps?.Dispose(); gs?.Dispose(); hs?.Dispose(); ds?.Dispose(); cs?.Dispose();
                dsv?.Dispose();
                predicate?.Dispose();
                DisposeAll(vsCb); DisposeAll(psCb); DisposeAll(csCb);
                DisposeAll(psSamplers); DisposeAll(csSamplers);
                DisposeAll(vsSrvs); DisposeAll(psSrvs); DisposeAll(csSrvs);
                DisposeAll(csUavs); DisposeAll(rtvs); DisposeAll(omUavs);
            }
        }

        private static void DisposeAll<T>(T[] objects) where T : IDisposable
        {
            foreach (var value in objects) value?.Dispose();
        }
    }
}
