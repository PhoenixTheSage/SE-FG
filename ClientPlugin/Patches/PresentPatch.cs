using System;
using ClientPlugin.FrameGen;
using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyRender11), "Present")]
internal static class PresentPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            FrameGenRuntime.OnPresent();
            FrameGenRuntime.NoteGamePresent();
        }
        catch (Exception e)
        {
            DebugLog.Write("Present prefix: " + e.GetType().Name + ": " + e.Message);
        }
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            FrameGenRuntime.OnPresented();
        }
        catch (Exception e)
        {
            DebugLog.Write("Present postfix: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
