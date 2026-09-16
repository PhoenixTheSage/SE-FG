using System;
using System.Diagnostics;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;

namespace ClientPlugin.FrameGen;

public static class FrameGenRuntime
{
    public static string LastBindingEvidence { get; private set; }
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static bool GeneratedThisFrame { get; private set; }
    public static int GenerateCount { get; private set; }
    public static int SkipCount { get; private set; }
    public static bool LastGenerateFailed { get; private set; }
    public static bool UsedExternalVelocity { get; private set; }
    public static string LastPath { get; private set; }

    private static bool _configChanged = true;
    private static bool _resetHistory = true;
    private static volatile bool _pluginsReady;
    private static int _consecutiveFails;
    private static string _lastVelocitySource;
    private static readonly float[] InvViewProj = new float[16];
    private static readonly float[] ViewProj = new float[16];
    private static readonly float[] PrevViewProj = new float[16];
    private static readonly Stopwatch PaceClock = Stopwatch.StartNew();
    private static long _lastPresentTicks;
    private static long _lastFrameTicks;

    public static bool WantsFrameGen
    {
        get
        {
            var config = Config.Current;
            if (config == null || !config.Enabled)
                return false;
            GpuSupport.TryProbe();
            if (!GpuSupport.CanAttemptFrameGen)
                return false;
            if (FrameGenHost.SupportKnown && !FrameGenHost.IsSupported)
                return false;
            return true;
        }
    }

    public static bool IsLive => WantsFrameGen && FrameGenHost.IsReady && !MyRender11.MultisamplingEnabled;

    public static void NotifyPluginsReady()
    {
        if (_pluginsReady)
            return;
        _pluginsReady = true;
        AnomalyHook.Probe();
        DebugLog.Write("plugins ready; FrameGen init allowed");
    }

    public static void NotifyConfigChanged()
    {
        _configChanged = true;
        _resetHistory = true;
        _consecutiveFails = 0;
        LastGenerateFailed = false;
        CameraHistory.Reset();
        AnomalyHook.InvalidateHistory();
        FrameGenHost.AllowRetry();
        DebugLog.Write("NotifyConfigChanged enabled=" + (Config.Current != null && Config.Current.Enabled));
    }

    public static void Shutdown()
    {
        FrameGenHost.Shutdown();
        CameraHistory.Reset();
        Width = Height = 0;
        _configChanged = true;
        _resetHistory = true;
        GeneratedThisFrame = false;
        GenerateCount = SkipCount = 0;
        LastBindingEvidence = LastPath = _lastVelocitySource = null;
    }

    public static bool TryPrepareFrame()
    {
        if (!_pluginsReady)
            return false;
        SnapshotSize();
        if (!WantsFrameGen)
        {
            if (Config.Current != null && Config.Current.Enabled)
            {
                if (GpuSupport.Probed && !GpuSupport.CanAttemptFrameGen)
                    FrameGenHost.LastError = GpuSupport.UnsupportedReason;
                else if (!FrameGenHost.IsLoaded)
                    FrameGenHost.LastError = "FrameGen is not enabled";
            }
            return false;
        }
        if (MyRender11.MultisamplingEnabled)
        {
            FrameGenHost.LastError = "FrameGen cannot run while MSAA is enabled.";
            return false;
        }
        var device = MyRender11.DeviceInstance;
        if (device == null)
        {
            FrameGenHost.LastError = "D3D11 device is not ready";
            return false;
        }
        try
        {
            if (!FrameGenHost.IsLoaded && !FrameGenHost.TryInit(device))
                return false;
        }
        catch (Exception e)
        {
            FrameGenHost.LastError = "FrameGen init threw: " + e.GetType().Name + ": " + e.Message;
            return false;
        }
        if (!FrameGenHost.IsSupported)
            return false;
        if (Width <= 0 || Height <= 0)
            return false;
        return FrameGenHost.TrySetSize((uint)Width, (uint)Height);
    }

    public static void SnapshotSize()
    {
        var bb = MyRender11.Backbuffer;
        if (bb != null && bb.Size.X > 0)
        {
            Width = bb.Size.X;
            Height = bb.Size.Y;
            return;
        }
        var res = MyRender11.ResolutionI;
        Width = res.X;
        Height = res.Y;
    }

    public static void OnPresent()
    {
        GeneratedThisFrame = false;
        if (!WantsFrameGen)
            return;
        if (!TryPrepareFrame() || !IsLive || _consecutiveFails >= 3)
            return;

        var rc = MyRender11.RC;
        var device = MyRender11.DeviceInstance;
        var backbuffer = MyRender11.Backbuffer;
        if (rc == null || device == null || rc.DeviceContext == null || backbuffer?.Resource == null)
            return;

        var gbuffer = MyGBuffer.Main;
        var depth = gbuffer?.ResolvedDepthStencil?.Resource;
        var color = backbuffer.Resource;
        if (depth == null || color == null)
            return;

        if (!TryGetTexture2D(color, out var colorTex))
            return;
        if (!FrameGenD3d.EnsureColorCopies(device, colorTex.Description))
            return;

        var context = rc.DeviceContext;
        context.CopyResource(color, FrameGenD3d.CurrCopy);

        CameraHistory.BeginFrame();
        AnomalyHook.BeginFrame();

        RenderTraceBind.Begin("FrameGen.Interpolate");
        try
        {
            var mvec = ResolveMotion(device, context, depth);
            var cameraCut = CameraHistory.ConsumeCameraCut();
            if (cameraCut)
                AnomalyHook.InvalidateHistory();
            var resetReason = VelocityAcceptance.ResetReason(
                _resetHistory, _configChanged, CameraHistory.HasPrevious,
                mvec == IntPtr.Zero && !UsedExternalVelocity, false, cameraCut);
            var reset = resetReason == "none" ? 0 : 1;
            _configChanged = false;
            _resetHistory = false;
            LastBindingEvidence = "Present source=" + (_lastVelocitySource ?? "none") + " reset=" + resetReason;

            FrameGenD3d.UnbindPipeline(context);
            var code = FrameGenHost.Interpolate(
                device, context, FrameGenD3d.CurrCopy, depth, mvec,
                FrameGenD3d.InterpCopy, (uint)Width, (uint)Height, reset);

            if (code < 0)
            {
                LastGenerateFailed = true;
                _consecutiveFails++;
                LastPath = "fail " + FrameGenHost.LastError;
                DebugLog.Write("Interpolate failed " + FrameGenHost.LastError);
                return;
            }

            _consecutiveFails = 0;
            LastGenerateFailed = false;
            if (code == 1)
            {
                SkipCount++;
                LastPath = "seed";
                return;
            }

            GenerateCount++;
            GeneratedThisFrame = true;
            LastPath = "interpolated";
            FrameGenD3d.UnbindPipeline(context);
            context.CopyResource(FrameGenD3d.InterpCopy, color);
            ExtraPresent();
            PaceUntilHalfFrame();
            var restored = MyRender11.Backbuffer?.Resource ?? color;
            FrameGenD3d.UnbindPipeline(context);
            context.CopyResource(FrameGenD3d.CurrCopy, restored);
        }
        catch (Exception e)
        {
            LastGenerateFailed = true;
            _consecutiveFails = 3;
            FrameGenHost.LastError = e.GetType().Name + ": " + e.Message;
            MyLog.Default.Error("FrameGen interpolate threw: " + e);
            RenderTraceBind.Dump("FrameGen.Interpolate", e);
        }
        finally
        {
            RenderTraceBind.End("FrameGen.Interpolate");
            rc.ClearState();
        }
    }

    private static IntPtr ResolveMotion(Device device, DeviceContext context, Resource depth)
    {
        UsedExternalVelocity = false;
        if (AnomalyHook.TryGetLive(Width, Height, out var externalMv, out _, out var srv))
        {
            var known = (srv as ISrvBindable)?.Resource;
            if (FrameGenD3d.ValidateVelocity(device, externalMv, Width, Height, out _, known))
            {
                UsedExternalVelocity = true;
                _lastVelocitySource = "Anomaly/" + AnomalyHook.SelectedSource;
                return externalMv;
            }
        }
        AnomalyHook.NoteCameraFallback();
        _lastVelocitySource = "camera";
        if (!CameraHistory.HasPrevious)
            return IntPtr.Zero;
        CameraHistory.CopyToArray(CameraHistory.InvViewProjection, InvViewProj);
        CameraHistory.CopyToArray(CameraHistory.ViewProjection, ViewProj);
        CameraHistory.CopyToArray(CameraHistory.PreviousViewProjection, PrevViewProj);
        return FrameGenHost.GenerateCameraMotionVectors(
            device, context, depth, (uint)Width, (uint)Height, InvViewProj, ViewProj, PrevViewProj);
    }

    private static void ExtraPresent()
    {
        var swap = MyRender11.m_swapchain;
        if (swap == null)
            return;
        swap.Present(0, PresentFlags.None);
        _lastPresentTicks = PaceClock.ElapsedTicks;
    }

    private static void PaceUntilHalfFrame()
    {
        var now = PaceClock.ElapsedTicks;
        if (_lastFrameTicks == 0)
        {
            _lastFrameTicks = now;
            return;
        }
        var frame = now - _lastFrameTicks;
        _lastFrameTicks = now;
        if (frame <= 0)
            return;
        var half = frame / 2;
        var deadline = _lastPresentTicks + half;
        var cap = Stopwatch.Frequency / 120; // never spin more than ~8ms
        var limit = now + cap;
        while (PaceClock.ElapsedTicks < deadline && PaceClock.ElapsedTicks < limit)
            System.Threading.Thread.SpinWait(64);
    }

    private static bool TryGetTexture2D(Resource resource, out Texture2D tex)
    {
        tex = resource as Texture2D;
        return tex != null && !tex.IsDisposed;
    }
}
