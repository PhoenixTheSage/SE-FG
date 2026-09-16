using System.Text;

namespace ClientPlugin.FrameGen;

public static class FrameGenStatus
{
    public static string CurrentText
    {
        get
        {
            var sb = new StringBuilder();
            AppendGpu(sb);
            sb.AppendLine();
            AppendHost(sb);
            sb.AppendLine();
            AppendConfig(sb);
            sb.AppendLine();
            AppendGenerate(sb);
            sb.AppendLine();
            AnomalyHook.AppendStatus(sb);
#if DEBUG
            if (!string.IsNullOrEmpty(DebugLog.FilePath))
            {
                sb.AppendLine();
                sb.Append("debug ").AppendLine(DebugLog.FilePath);
            }
#endif
            return sb.ToString();
        }
    }

    static void AppendGpu(StringBuilder sb)
    {
        sb.Append("GPU  ").Append(GpuSupport.StatusLine);
        sb.Append(" · eligible ").AppendLine(Yes(GpuSupport.CanOfferFrameGen));
    }

    static void AppendHost(StringBuilder sb)
    {
        sb.Append("FG   ");
        if (FrameGenHost.IsReady)
            sb.Append("ready");
        else if (FrameGenHost.IsLoaded && FrameGenHost.IsSupported)
            sb.Append("loaded, not ready");
        else if (FrameGenHost.IsLoaded)
            sb.Append("loaded, unsupported");
        else
            sb.Append("not loaded");
        sb.Append(" · ").AppendLine(FrameGenHost.CurrentPresetHint);

        var err = FrameGenHost.LastError;
        if (!string.IsNullOrEmpty(err) && !(FrameGenHost.IsReady && err == "not initialized"))
            sb.Append("     ").AppendLine(err);
    }

    static void AppendConfig(StringBuilder sb)
    {
        var cfg = Config.Current;
        sb.Append("On   ").AppendLine(Yes(cfg != null && cfg.Enabled));
        sb.Append("Res  ").Append(FrameGenRuntime.Width).Append('x').AppendLine(FrameGenRuntime.Height.ToString());
    }

    static void AppendGenerate(StringBuilder sb)
    {
        sb.Append("Gen  ");
        if (FrameGenRuntime.GenerateCount <= 0 && FrameGenRuntime.SkipCount <= 0)
            sb.AppendLine("none");
        else
        {
            sb.Append("interpolated ×").Append(FrameGenRuntime.GenerateCount);
            sb.Append(" · seed ×").Append(FrameGenRuntime.SkipCount);
            if (!string.IsNullOrEmpty(FrameGenRuntime.LastPath))
                sb.Append(" · ").Append(FrameGenRuntime.LastPath);
            sb.AppendLine();
        }

        sb.Append("     Anomaly velocity ").AppendLine(Yes(FrameGenRuntime.UsedExternalVelocity));
        if (FrameGenRuntime.LastGenerateFailed)
            sb.AppendLine("     last interpolate failed; presenting real frames only");
    }

    static string Yes(bool value) => value ? "yes" : "no";
}
