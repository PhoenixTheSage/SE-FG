using ClientPlugin.Settings;

namespace ClientPlugin.RichHud;

/// <summary>
/// Shared get/set/save for Pulsar and Rich HUD controls.
/// </summary>
internal static class RichHudOptions
{
    public static bool GetEnabled() => Config.Current.Enabled;

    public static void SetEnabled(bool value)
    {
        Config.Current.Enabled = value;
        Save();
    }

    public static void ShowStatus()
    {
        Config.ShowStatus();
    }

    public static void Save()
    {
        ConfigStorage.Save(Config.Current);
    }
}
