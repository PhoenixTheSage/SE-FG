using System;
using ClientPlugin.FrameGen;
using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyRender11), "Draw", typeof(bool))]
internal static class DrawPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            GpuSupport.TryProbe();
            FrameGenRuntime.SnapshotSize();
            if (FrameGenRuntime.WantsFrameGen)
                FrameGenRuntime.TryPrepareFrame();
        }
        catch (Exception e)
        {
            DebugLog.Write("Draw prefix: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
