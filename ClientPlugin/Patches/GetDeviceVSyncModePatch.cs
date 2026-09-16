using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

// Bypass Keen's monitor probe, which P/Invokes the unavailable PSNative.dll.
[HarmonyPatch(typeof(MyRender11), nameof(MyRender11.GetDeviceVSyncMode))]
internal static class GetDeviceVSyncModePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref int __result)
    {
        __result = MyRender11.DeviceSettings.VSync;
        return false;
    }
}
