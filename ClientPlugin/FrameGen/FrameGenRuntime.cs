using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ClientPlugin.Patches;
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
    public static float GameFps { get; private set; }
    public static float DisplayFps { get; private set; }
    public static int DisplayRefreshHz { get; private set; }

    private static bool _configChanged = true;
    private static bool _resetHistory = true;
    private static volatile bool _pluginsReady;
    private static int _consecutiveFails;
    private static string _lastVelocitySource;
    private static readonly float[] InvViewProj = new float[16];
    private static readonly float[] ViewProj = new float[16];
    private static readonly float[] PrevViewProj = new float[16];
    private static bool _haveInterp;
    private static bool _haveScene;
    private static readonly Stopwatch FpsClock = Stopwatch.StartNew();
    private static int _gamePresents;
    private static int _displayPresents;
    private static long _fpsWindowStart;
    private static long _lastGamePresentTicks;
    private static bool _refreshCapped;
    private static int _cachedRefreshHz;
    private static long _refreshProbeTicks;

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
        _haveInterp = false;
        _haveScene = false;
        _refreshCapped = false;
        CameraHistory.Reset();
        AnomalyHook.InvalidateHistory();
        FrameGenHost.AllowRetry();
        DebugLog.Write("NotifyConfigChanged enabled=" + (Config.Current != null && Config.Current.Enabled));
    }

    public static void Shutdown()
    {
        FrameGenHost.Shutdown();
        PostPpHudPass.Reset();
        CameraHistory.Reset();
        Width = Height = 0;
        _configChanged = true;
        _resetHistory = true;
        GeneratedThisFrame = false;
        _haveInterp = false;
        _haveScene = false;
        GenerateCount = SkipCount = 0;
        LastBindingEvidence = LastPath = _lastVelocitySource = null;
        GameFps = DisplayFps = 0;
        DisplayRefreshHz = 0;
        _gamePresents = _displayPresents = 0;
        _fpsWindowStart = 0;
        _lastGamePresentTicks = 0;
        _refreshCapped = false;
        _cachedRefreshHz = 0;
        _refreshProbeTicks = 0;
    }

    public static string GetOverlayText()
    {
        var config = Config.Current;
        if (config == null || !config.Enabled || !config.ShowOverlay)
            return null;

        RefreshFpsWindow();
        if (string.Equals(LastPath, "vsync", StringComparison.Ordinal))
            return "FG  " + GameFps.ToString("0") + " game · VSync";
        if (string.Equals(LastPath, "refresh-cap", StringComparison.Ordinal))
            return "FG  " + GameFps.ToString("0") + " game · " + DisplayRefreshHz + " Hz";
        return "FG  " + GameFps.ToString("0") + " game · " + DisplayFps.ToString("0") + " disp";
    }

    public static void NoteGamePresent()
    {
        if (Config.Current == null || !Config.Current.Enabled)
            return;
        _lastGamePresentTicks = FpsClock.ElapsedTicks;
        _gamePresents++;
        _displayPresents++;
        RefreshFpsWindow();
    }

    public static void NoteExtraPresent()
    {
        _displayPresents++;
        RefreshFpsWindow();
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

    /// <summary>
    /// DXGI swapchain size, not <see cref="MyRender11.Backbuffer"/>.Size.
    /// DLSS/FRS SetDRS makes Backbuffer.Size follow internal ResolutionI
    /// while the swapchain texture stays at output.
    /// </summary>
    public static void SnapshotSize()
    {
        try
        {
            if (MyRender11.Backbuffer?.Resource is Texture2D tex && !tex.IsDisposed)
            {
                var desc = tex.Description;
                if (desc.Width > 0 && desc.Height > 0)
                {
                    Width = desc.Width;
                    Height = desc.Height;
                    return;
                }
            }
        }
        catch
        {
            // Device tearing down.
        }

        try
        {
            var settings = MyRender11.DeviceSettings;
            if (settings.BackBufferWidth > 0 && settings.BackBufferHeight > 0)
            {
                Width = settings.BackBufferWidth;
                Height = settings.BackBufferHeight;
                return;
            }
        }
        catch
        {
            // Settings not ready.
        }

        var res = MyRender11.ResolutionI;
        Width = res.X;
        Height = res.Y;
    }

    public static void BeginSceneCapture()
    {
        _haveScene = false;
    }

    /// <summary>
    /// Snapshot 3D color from the backbuffer after <c>DrawScene</c> /
    /// CopyToRT. Keen PostPP is skipped while live, so this is scene without
    /// Rich HUD. Do not copy the in-flight LDR target from RenderPostPP.
    /// </summary>
    public static void CaptureScene(Resource source = null)
    {
        if (!WantsFrameGen || MyRender11.MultisamplingEnabled)
            return;

        var rc = MyRender11.RC;
        var device = MyRender11.DeviceInstance;
        var color = source ?? MyRender11.Backbuffer?.Resource;
        if (rc?.DeviceContext == null || device == null || color == null)
            return;
        if (!TryGetTexture2D(color, out var colorTex))
            return;

        var bb = MyRender11.Backbuffer?.Resource;
        if (bb == null || !TryGetTexture2D(bb, out var bbTex))
            return;
        if (!FrameGenD3d.EnsureColorCopies(device, bbTex.Description))
            return;
        if (colorTex.Description.Width != bbTex.Description.Width ||
            colorTex.Description.Height != bbTex.Description.Height ||
            colorTex.Description.Format != bbTex.Description.Format)
            return;

        FrameGenD3d.CopyFromBackbuffer(rc.DeviceContext, color, FrameGenD3d.SceneCopy);
        _haveScene = true;
    }

    public static void CaptureSceneFallback()
    {
        if (_haveScene)
            return;
        CaptureScene();
    }

    public static void OnPresent()
    {
        GeneratedThisFrame = false;
        _haveInterp = false;
        if (!WantsFrameGen)
            return;
        SnapshotSize();
        if (!TryPrepareFrame() || !IsLive || _consecutiveFails >= 3)
            return;
        if (GameSyncInterval() != 0)
        {
            LastPath = "vsync";
            FrameGenHost.LastError = "Turn VSync off. Extra Present with VSync on waits a refresh and halves FPS.";
            return;
        }
        if (ShouldSkipExtraForRefresh())
        {
            LastPath = "refresh-cap";
            _resetHistory = true;
            return;
        }
        if (!_haveScene || FrameGenD3d.SceneCopy == null)
        {
            LastPath = "no-scene";
            return;
        }

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
        FrameGenD3d.CopyFromBackbuffer(context, color, FrameGenD3d.HudCopy);

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
                device, context, FrameGenD3d.SceneCopy, depth, mvec,
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
            _haveInterp = true;
            LastPath = "interpolated";
        }
        catch (Exception e)
        {
            LastGenerateFailed = true;
            _consecutiveFails = 3;
            _haveInterp = false;
            FrameGenHost.LastError = e.GetType().Name + ": " + e.Message;
            MyLog.Default.Error("FrameGen interpolate threw: " + e);
            RenderTraceBind.Dump("FrameGen.Interpolate", e);
        }
        finally
        {
            RenderTraceBind.End("FrameGen.Interpolate");
            try
            {
                FrameGenD3d.UnbindPipeline(context);
            }
            catch
            {
                // Device already torn down.
            }
        }
    }

    public static void OnPresented()
    {
        if (!_haveInterp)
            return;
        _haveInterp = false;
        if (GameSyncInterval() != 0)
        {
            LastPath = "interpolated-vsync-skip";
            return;
        }

        var rc = MyRender11.RC;
        var device = MyRender11.DeviceInstance;
        var dest = MyRender11.Backbuffer?.Resource;
        if (rc?.DeviceContext == null || device == null || dest == null)
            return;

        var context = rc.DeviceContext;
        try
        {
            if (!FrameGenD3d.BlitGeneratedFrame(device, context, dest))
            {
                LastGenerateFailed = true;
                _consecutiveFails++;
                LastPath = "blit-fail";
                return;
            }
            ExtraPresent();
            LastPath = "interpolated-after-present";
        }
        catch (Exception e)
        {
            LastGenerateFailed = true;
            _consecutiveFails++;
            FrameGenHost.LastError = "extra Present: " + e.GetType().Name + ": " + e.Message;
            DebugLog.Write("Present postfix: " + FrameGenHost.LastError);
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
        NoteExtraPresent();
    }

    /// <summary>
    /// Extra Present is a second flip in the same game frame. On a fixed-Hz
    /// panel, when the game already fills the refresh, that interpolant is
    /// queued on top of the real frame (ghosting), not a unique display slot.
    /// </summary>
    private static bool ShouldSkipExtraForRefresh()
    {
        var hz = ProbeDisplayRefreshHz();
        DisplayRefreshHz = hz;
        if (hz < 30 || _lastGamePresentTicks == 0)
            return false;

        var dt = FpsClock.ElapsedTicks - _lastGamePresentTicks;
        if (dt <= 0)
            return _refreshCapped;

        var period = Stopwatch.Frequency / hz;
        if (_refreshCapped)
            _refreshCapped = dt < period * 125L / 100L;
        else
            _refreshCapped = dt < period * 112L / 100L;
        return _refreshCapped;
    }

    private static int ProbeDisplayRefreshHz()
    {
        var now = FpsClock.ElapsedTicks;
        if (_cachedRefreshHz >= 30 && now - _refreshProbeTicks < Stopwatch.Frequency * 2)
            return _cachedRefreshHz;

        _refreshProbeTicks = now;
        var hz = 0;
        try
        {
            var swap = MyRender11.m_swapchain;
            if (swap != null)
            {
                hz = RationalHz(swap.Description.ModeDescription.RefreshRate);
                if (hz < 30)
                    hz = RefreshFromOutput(swap);
            }
        }
        catch
        {
            hz = 0;
        }

        if (hz < 30)
            hz = RefreshFromDesktop();
        if (hz >= 30)
            _cachedRefreshHz = hz;
        return _cachedRefreshHz;
    }

    private static int RefreshFromOutput(SwapChain swap)
    {
        Output output = null;
        try
        {
            output = swap.ContainingOutput;
            if (output == null)
                return 0;
            var target = new ModeDescription
            {
                Width = Width > 0 ? Width : swap.Description.ModeDescription.Width,
                Height = Height > 0 ? Height : swap.Description.ModeDescription.Height,
                Format = swap.Description.ModeDescription.Format,
                RefreshRate = new Rational(0, 0),
                Scaling = DisplayModeScaling.Unspecified,
                ScanlineOrdering = DisplayModeScanlineOrder.Unspecified
            };
            var device = MyRender11.DeviceInstance;
            if (device == null)
                return 0;
            output.GetClosestMatchingMode(device, target, out var closest);
            return RationalHz(closest.RefreshRate);
        }
        catch
        {
            return 0;
        }
        finally
        {
            output?.Dispose();
        }
    }

    private static int RationalHz(Rational rate)
    {
        if (rate.Denominator <= 0 || rate.Numerator <= 0)
            return 0;
        var hz = (int)Math.Round(rate.Numerator / (double)rate.Denominator);
        return hz is >= 30 and <= 500 ? hz : 0;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DeviceMode devMode);

    private static int RefreshFromDesktop()
    {
        try
        {
            var mode = new DeviceMode { Size = (short)Marshal.SizeOf<DeviceMode>() };
            if (!EnumDisplaySettings(null, -1, ref mode))
                return 0;
            return mode.DisplayFrequency is >= 30 and <= 500 ? mode.DisplayFrequency : 0;
        }
        catch
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
    }

    private static void RefreshFpsWindow()
    {
        var now = FpsClock.ElapsedTicks;
        if (_fpsWindowStart == 0)
        {
            _fpsWindowStart = now;
            return;
        }

        var elapsed = now - _fpsWindowStart;
        var window = Stopwatch.Frequency / 2;
        if (elapsed < window)
            return;

        var seconds = (float)elapsed / Stopwatch.Frequency;
        if (seconds > 0f)
        {
            GameFps = _gamePresents / seconds;
            DisplayFps = _displayPresents / seconds;
        }

        _gamePresents = 0;
        _displayPresents = 0;
        _fpsWindowStart = now;
    }

    private static int GameSyncInterval()
    {
        try
        {
            return MyRender11.DeviceSettings.VSync;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetTexture2D(Resource resource, out Texture2D tex)
    {
        tex = resource as Texture2D;
        return tex != null && !tex.IsDisposed;
    }
}
