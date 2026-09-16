using System;
using System.Reflection;
using ClientPlugin.FrameGen;
using VRage.Utils;

namespace ClientPlugin.RichHud;

/// <summary>
/// Optional bind to Anomaly <c>ClientPlugin.RichHud.HudOverlayRegistry</c>.
/// Safe when Anomaly or Master is absent.
/// </summary>
internal static class HudOverlayBind
{
    public const string RegistryTypeName = "ClientPlugin.RichHud.HudOverlayRegistry";
    public const string OverlayId = "se-framegen";

    static readonly object Gate = new();
    static bool _installed;

    public static bool Installed
    {
        get
        {
            lock (Gate)
                return _installed;
        }
    }

    public static bool TryInstall()
    {
        lock (Gate)
        {
            if (_installed)
                return true;

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch
            {
                return false;
            }

            foreach (var assembly in assemblies)
            {
                if (assembly == null || assembly == typeof(HudOverlayBind).Assembly)
                    continue;

                Type registry;
                try
                {
                    registry = assembly.GetType(RegistryTypeName, throwOnError: false, ignoreCase: false);
                }
                catch
                {
                    continue;
                }

                var register = registry?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(Func<string>) }, null);
                if (register == null)
                    continue;

                try
                {
                    var ok = register.Invoke(null, new object[]
                    {
                        OverlayId,
                        (Func<string>)FrameGenRuntime.GetOverlayText,
                    });
                    if (ok is false)
                        return false;
                    _installed = true;
                    MyLog.Default.WriteLine("FrameGen: Rich HUD overlay " + OverlayId);
                    DebugLog.Write("HudOverlayRegistry " + OverlayId);
                    return true;
                }
                catch (Exception e)
                {
                    DebugLog.Write("HudOverlayRegistry.Register: " + e.GetType().Name + ": " + e.Message);
                    return false;
                }
            }

            return false;
        }
    }

    public static void Reset()
    {
        lock (Gate)
            _installed = false;
    }
}
