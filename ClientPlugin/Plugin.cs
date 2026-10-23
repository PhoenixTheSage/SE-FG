using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.FrameGen;
using ClientPlugin.RichHud;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SpaceEngineersFrameGen.Dx11")]

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.5.2.0")]
[assembly: AssemblyFileVersion("1.5.2.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public sealed class Plugin : IPlugin
{
    public const string Name = "SpaceEngineersFrameGen";
    public static Plugin Instance { get; private set; }

    private SettingsGenerator settingsGenerator;
    private bool disposed;
    private bool configUiDirty;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        disposed = false;
        Instance = this;
        settingsGenerator = new SettingsGenerator();
        DebugLog.Open();
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
        if (AnomalyRenderer.Decided && !AnomalyRenderer.AllowDx11Device)
            MyLog.Default.WriteLine(ClosedRoute("FrameGen"));
    }

    internal static void RefreshConfigUi()
    {
        AnomalyTerminalHook.TryRefresh();
        var instance = Instance;
        if (instance == null || instance.disposed)
            return;
        instance.configUiDirty = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        DebugLog.Write("Dispose");
        RequestRenderer(2, false);
        if (AnomalyRenderer.AllowDx11Device)
            Dx11Bridge.Call("OnDispose");
        ConfigStorage.FlushPending(true);
        AnomalyTerminalHook.Reset();
        HudOverlayBind.Reset();
        SatelliteAssembly.Reset();
        settingsGenerator = null;
        if (ReferenceEquals(Instance, this))
            Instance = null;
        DebugLog.Close();
    }

    public void Update()
    {
        PluginStatusPage.TryInstall("FrameGen", Config.ShowStatus);
        if (disposed)
            return;
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
        FlushConfigUi();
        ConfigStorage.FlushPending();
        RequestRenderer(2, AnomalyRenderer.AllowGuestWork && Config.Current.Enabled);
        if (!AnomalyRenderer.AllowDx11Device)
            return;
        Dx11Bridge.Call("OnUpdate");
    }

    void FlushConfigUi()
    {
        if (!configUiDirty)
            return;
        configUiDirty = false;
        var dialog = settingsGenerator?.Dialog;
        if (dialog == null)
            return;
        try
        {
            if (dialog.State == MyGuiScreenState.OPENED)
                dialog.RecreateControls(false);
        }
        catch
        {
            // Dialog not on the stack.
        }
    }

    public void OpenConfigDialog()
    {
        var generator = settingsGenerator;
        if (disposed || generator == null)
            return;
        if (AnomalyRenderer.AllowDx11Device)
            Dx11Bridge.Call("PrepareConfig");
        generator.SetLayout<Simple>();
        generator.Dialog.RecreateControls(true);
        MyGuiSandbox.AddScreen(generator.Dialog);
    }

    static void RequestRenderer(int bit, bool enabled)
    {
        if (!AnomalyRenderer.AllowGuestWork)
            return;
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("ClientPlugin.Shaders.DisplayServices", false);
            if (type == null)
                continue;
            type.GetMethod(enabled ? "Request" : "Release")?.Invoke(null, new object[] { bit });
            return;
        }
    }

    static string ClosedRoute(string name) =>
        AnomalyRenderer.LinuxRequiresGuest
            ? name + ": Linux stays idle until Anomaly's Vulkan guest bus is open"
            : AnomalyRenderer.AllowGuestWork
                ? name + ": Vulkan runs the DX11 warp and an extra immediate present. Show Status reads that path."
                : name + ": DX11 device path closed (" + AnomalyRenderer.ActiveName + ")";

    public void LoadAssets(IReadOnlyDictionary<string, string> assets)
    {
        if (disposed)
            return;
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
    }

    public void LoadAssets(string folder)
    {
        if (disposed)
            return;
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
    }
}
