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
        FrameGenRuntime.BeginSceneCapture();
        PostPpHudPass.BeginDraw();
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            FrameGenRuntime.CaptureSceneFallback();
            PostPpHudPass.TryDrawAfterSceneBlit();
        }
        catch (Exception e)
        {
            DebugLog.Write("DrawScene postfix: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
