using System;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.FrameGen;

/// <summary>
/// Detects the game's D3D11 adapter. FrameGen is vendor-agnostic.
/// </summary>
public static class GpuSupport
{
    public const int VendorNvidia = 0x10DE;
    public const int VendorAmd = 0x1002;
    public const int VendorIntel = 0x8086;
    public const int VendorMicrosoft = 0x1414;

    public static bool Probed { get; private set; }
    public static int VendorId { get; private set; }
    public static string AdapterName { get; private set; } = "unknown";
    public static string VendorName { get; private set; } = "unknown";
    public static bool IsNvidia { get; private set; }
    public static bool IsAmd { get; private set; }
    public static bool IsWarp { get; private set; }
    public static FeatureLevel FeatureLevel { get; private set; }

    public static bool CanAttemptFrameGen => Probed && !IsWarp && FeatureLevel >= FeatureLevel.Level_11_0;

    public static bool CanOfferFrameGen
    {
        get
        {
            TryProbe();
            if (Probed && !CanAttemptFrameGen)
                return false;
            return !FrameGenHost.SupportKnown || FrameGenHost.IsSupported;
        }
    }

    public static string UnsupportedReason
    {
        get
        {
            if (!Probed)
                return "GPU has not been detected yet";
            if (IsWarp)
                return "FrameGen cannot run on the Microsoft Basic Render Driver";
            if (FeatureLevel < FeatureLevel.Level_11_0)
                return "FrameGen requires Direct3D feature level 11_0 or newer";
            return null;
        }
    }

    public static string StatusLine
    {
        get
        {
            if (!Probed)
                return "not detected yet";
            return VendorName + " " + AdapterName + " (0x" + VendorId.ToString("X4") + ", " + FeatureLevel + ")";
        }
    }

    public static bool TryProbe()
    {
        if (Probed)
            return true;
        return TryProbe(MyRender11.DeviceInstance);
    }

    public static bool TryProbe(Device device)
    {
        if (Probed)
            return true;
        if (device == null)
            return false;

        try
        {
            using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
            using var adapter = dxgiDevice.Adapter;
            var desc = adapter.Description;
            VendorId = desc.VendorId;
            AdapterName = string.IsNullOrEmpty(desc.Description) ? "unknown" : desc.Description.Trim();
            VendorName = NameForVendor(VendorId);
            IsNvidia = VendorId == VendorNvidia;
            IsAmd = VendorId == VendorAmd;
            IsWarp = VendorId == VendorMicrosoft;
            FeatureLevel = device.FeatureLevel;
            Probed = true;
            DebugLog.Write("GPU " + VendorName + " vendor=0x" + VendorId.ToString("X4") +
                           " fl=" + FeatureLevel + " " + AdapterName);
            var reason = UnsupportedReason;
            if (reason != null)
            {
                FrameGenHost.LastError = reason;
                MyLog.Default.Warning("FrameGen: " + reason);
                DebugLog.Write("FrameGen blocked: " + reason);
            }
            return true;
        }
        catch (Exception e)
        {
            var error = "GPU probe failed: " + e.GetType().Name + ": " + e.Message;
            FrameGenHost.LastError = error;
            DebugLog.Write(error);
            return false;
        }
    }

    internal static void Reset()
    {
        Probed = false;
        VendorId = 0;
        AdapterName = "unknown";
        VendorName = "unknown";
        IsNvidia = false;
        IsAmd = false;
        IsWarp = false;
        FeatureLevel = 0;
    }

    internal static string NameForVendor(int vendorId)
    {
        switch (vendorId)
        {
            case VendorNvidia: return "NVIDIA";
            case VendorAmd: return "AMD";
            case VendorIntel: return "Intel";
            case VendorMicrosoft: return "Microsoft";
            default: return "vendor 0x" + vendorId.ToString("X4");
        }
    }
}
