using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.FrameGen;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using Sandbox.Graphics.GUI;

namespace ClientPlugin;

public class Config : INotifyPropertyChanged
{
    #region Options

    private bool enabled = true;
    private bool showOverlay = true;

    #endregion

    #region User interface

    public readonly string Title = "FrameGen";

    [Separator("Frame Generation")]

    [Checkbox(label: "Enabled",
        description: "Insert an interpolated frame after each real Present. Turn VSync off; with VSync on the extra Present waits a refresh and is skipped.")]
    public bool Enabled
    {
        get => enabled;
        set => SetField(ref enabled, value);
    }

    [Checkbox(label: "Show FPS overlay",
        description: "Corner FPS line when Anomaly and Rich HUD Master are loaded (game FPS and displayed FPS).")]
    public bool ShowOverlay
    {
        get => showOverlay;
        set => SetField(ref showOverlay, value);
    }

    [Separator("Status")]

    [Button(label: "Show Status", description: "GPU, FrameGen context, and Anomaly velocity status")]
    // ReSharper disable once UnusedMember.Global
    public static void ShowStatus()
    {
        GpuSupport.TryProbe();
        MyGuiSandbox.AddScreen(new StatusScreen(FrameGenStatus.CurrentText));
    }

    #endregion

    #region Property change notification boilerplate

    public static readonly Config Default = new();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(Enabled))
        {
            DebugLog.Write("config Enabled=" + enabled);
            FrameGenRuntime.NotifyConfigChanged();
        }
        Plugin.RefreshConfigUi();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    #endregion
}
