using System;
using System.Reflection;
using ClientPlugin.Patches;
using HarmonyLib;
using VRage.Utils;

namespace ClientPlugin.FrameGen;

/// <summary>DX11 frame warp and extra Present. Loaded only beside the main plugin on Windows.</summary>
public static class Dx11Entry
{
    static Harmony harmony;
    static Harmony deviceHarmony;
    static bool routed;
    static bool failed;
    static string initializationError;

    public static void OnUpdate()
    {
        if (failed)
            return;
        try
        {
            if (!routed)
            {
                routed = true;
                GpuSupport.TryProbe();
                harmony = new Harmony(Plugin.Name);
                harmony.PatchAll(typeof(Dx11Entry).Assembly);
                deviceHarmony = new Harmony(DeviceDisposePatch.HarmonyId);
                DeviceDisposePatch.Apply(deviceHarmony);
                AnomalyHook.Probe();
                MyLog.Default.WriteLine("FrameGen plugin initialized. GPU: " + GpuSupport.StatusLine);
                DebugLog.Write("Harmony patched, plugin initialized GPU=" + GpuSupport.StatusLine);
            }
            FrameGenRuntime.NotifyPluginsReady();
        }
        catch (Exception e)
        {
            failed = true;
            var root = e.GetBaseException();
            initializationError = root.ToString();
            MyLog.Default.Error("FrameGen DX11 update failed; device path disabled this session: " +
                                root.GetType().Name + ": " + root.Message);
        }
    }

    public static void OnDispose()
    {
        FrameGenRuntime.Shutdown();
        GpuSupport.Reset();
    }

    public static void PrepareConfig() => GpuSupport.TryProbe();

    public static void NotifyConfigChanged() => FrameGenRuntime.NotifyConfigChanged();

    public static string StatusText()
    {
        if (failed)
            return "FrameGen DX11 initialization failed:\n" + initializationError;
        GpuSupport.TryProbe();
        return FrameGenStatus.CurrentText;
    }

    public static string OverlayText() => FrameGenRuntime.GetOverlayText();
}
