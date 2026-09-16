using System;
using SharpDX.Direct3D11;
using VRage.Utils;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;

namespace ClientPlugin.FrameGen;

public static class FrameGenHost
{
    public static bool IsLoaded { get; private set; }
    public static bool IsSupported { get; private set; }
    public static bool IsReady { get; private set; }
    public static bool SupportKnown { get; private set; }
    public static string LastError { get; internal set; } = "not initialized";
    public static string CurrentPresetHint => "FrameGen DX11 SharpDX";

    private static bool _tornDown;
    private static Device _deviceOwner;
    private static bool _initBlocked;
    private static uint _lastW;
    private static uint _lastH;

    internal static void SetError(string text)
    {
        LastError = string.IsNullOrEmpty(text) ? "unknown error" : text;
    }

    public static bool TryInit(Device device)
    {
        if (IsLoaded)
            return IsSupported;
        if (_initBlocked)
            return false;
        if (device == null || device.IsDisposed)
        {
            LastError = "D3D11 device is not ready";
            return false;
        }

        GpuSupport.TryProbe(device);
        if (!GpuSupport.CanAttemptFrameGen)
        {
            LastError = GpuSupport.UnsupportedReason;
            SupportKnown = true;
            IsSupported = false;
            return false;
        }

        try
        {
            if (!FrameGenD3d.EnsureInterpolator(device, 8, 8))
            {
                _initBlocked = true;
                SupportKnown = true;
                IsSupported = false;
                return false;
            }
        }
        catch (Exception e)
        {
            LastError = "FrameGen init threw: " + e.GetType().Name + ": " + e.Message;
            _initBlocked = true;
            SupportKnown = true;
            IsSupported = false;
            return false;
        }

        IsLoaded = true;
        _deviceOwner = device;
        _tornDown = false;
        IsSupported = true;
        SupportKnown = true;
        LastError = "ok";
        DebugLog.Write("FrameGen Init ok SharpDX compute interpolator");
        return true;
    }

    public static bool TrySetSize(uint width, uint height)
    {
        if (!IsSupported || _deviceOwner == null)
            return false;
        if (IsReady && _lastW == width && _lastH == height)
            return true;
        if (!FrameGenD3d.EnsureInterpolator(_deviceOwner, width, height))
        {
            IsReady = false;
            return false;
        }
        _lastW = width;
        _lastH = height;
        IsReady = true;
        if (LastError == "not initialized")
            LastError = "ok";
        return true;
    }

    public static int Interpolate(
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
        if (!IsReady || device == null || context == null || color == null || output == null)
        {
            LastError = "Interpolate missing device, color, or output";
            return -1;
        }

        var motion = FrameGenD3d.EnsureMotionOrZero(device, context, motionVectors, width, height);
        FrameGenD3d.UnbindPipeline(context);
        var code = FrameGenD3d.Interpolate(
            device, context, color, depth, motion, output, width, height, reset);
        return code;
    }

    public static IntPtr GenerateCameraMotionVectors(
        Device device, DeviceContext context, Resource depth, uint width, uint height,
        float[] invViewProj, float[] viewProj, float[] prevViewProj)
    {
        if (!IsLoaded)
            return IntPtr.Zero;
        return FrameGenD3d.GenerateCameraMotionVectors(
            device, context, depth, width, height, invViewProj, viewProj, prevViewProj);
    }

    public static void AllowRetry()
    {
        _initBlocked = false;
        SupportKnown = IsLoaded;
    }

    public static void Shutdown()
    {
        if (IsLoaded && !_tornDown)
        {
            try
            {
                FrameGenD3d.Release();
            }
            catch (Exception e)
            {
                DebugLog.Write("FrameGenHost.Shutdown: " + e.GetType().Name);
            }
            _tornDown = true;
        }
        ResetSessionFlags();
    }

    public static void OnDeviceDisposing(Device device)
    {
        if (device == null || !ReferenceEquals(device, _deviceOwner))
            return;
        if (!IsLoaded || _tornDown)
            return;
        try
        {
            FrameGenD3d.Release();
        }
        catch (Exception e)
        {
            MyLog.Default.Error("FrameGen shutdown failed: " + e);
        }
        _tornDown = true;
        _deviceOwner = null;
        ResetSessionFlags();
    }

    private static void ResetSessionFlags()
    {
        IsLoaded = false;
        IsSupported = false;
        IsReady = false;
        SupportKnown = false;
        _initBlocked = false;
        _lastW = _lastH = 0;
        LastError = "shutdown";
    }
}
