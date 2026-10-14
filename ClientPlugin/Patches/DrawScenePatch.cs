using System;
using ClientPlugin.FrameGen;
using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyRender11), nameof(MyRender11.DrawScene))]
internal static class DrawScenePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            FrameGenRuntime.BeginSceneCapture();
            PostPpHudPass.BeginDraw();
        }
        catch (Exception e)
        {
            DebugLog.Write("DrawScene prefix: " + e.GetType().Name + ": " + e.Message);
        }
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Postfix()
    {
        try
        {
            FrameGenRuntime.CaptureAfterCopyToRt();
            FrameGenRuntime.CaptureSceneFallback();
            PostPpHudPass.TryDrawAfterSceneBlit();
        }
        catch (Exception e)
        {
            DebugLog.Write("DrawScene postfix: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
