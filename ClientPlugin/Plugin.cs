using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.FrameGen;
using ClientPlugin.Patches;
using ClientPlugin.RichHud;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public sealed class Plugin : IPlugin
{
    public const string Name = "SpaceEngineersFrameGen";
    public static Plugin Instance { get; private set; }

    private SettingsGenerator settingsGenerator;
    private Harmony harmony;
    private Harmony deviceHarmony;
    private bool disposed;
    private bool configUiDirty;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        disposed = false;
        Instance = this;
        settingsGenerator = new SettingsGenerator();
        DebugLog.Open();

        GpuSupport.TryProbe();
        AnomalyHook.Probe();
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();

        harmony = new Harmony(Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        deviceHarmony = new Harmony(DeviceDisposePatch.HarmonyId);
        DeviceDisposePatch.Apply(deviceHarmony);
        AnomalyHook.Probe();
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
        MyLog.Default.WriteLine("FrameGen plugin initialized. GPU: " + GpuSupport.StatusLine);
        DebugLog.Write("Harmony patched, plugin initialized GPU=" + GpuSupport.StatusLine);
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
        harmony = null;
        // Leave Harmony patches in place. Pulsar only disposes plugins at
        // process exit; UnpatchAll rewrites shared trampolines while other
        // plugins may still be running. DeviceDisposePatch stays applied so
        // FrameGen can shut down when the D3D device is released.
        ConfigStorage.FlushPending(true);
        FrameGenRuntime.Shutdown();
        AnomalyTerminalHook.Reset();
        HudOverlayBind.Reset();
        GpuSupport.Reset();
        settingsGenerator = null;
        if (ReferenceEquals(Instance, this))
            Instance = null;
        DebugLog.Close();
    }

    public void Update()
    {
        if (disposed)
            return;
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
        FlushConfigUi();
        ConfigStorage.FlushPending();
        FrameGenRuntime.NotifyPluginsReady();
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

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        var generator = settingsGenerator;
        if (disposed || generator == null)
            return;

        GpuSupport.TryProbe();
        generator.SetLayout<Simple>();
        generator.Dialog.RecreateControls(true);
        MyGuiSandbox.AddScreen(generator.Dialog);
    }

    // Pulsar still calls these; FrameGen has no native asset DLL.
    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(IReadOnlyDictionary<string, string> assets)
    {
        if (disposed)
            return;
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
    }

    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(string folder)
    {
        if (disposed)
            return;
        AnomalyTerminalHook.TryInstall();
        HudOverlayBind.TryInstall();
    }
}
