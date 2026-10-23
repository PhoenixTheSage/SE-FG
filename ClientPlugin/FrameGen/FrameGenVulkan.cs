using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClientPlugin.FrameGen;

/// <summary>
/// Debug status for the renderer warp. The page matches the DX11 status fields.
/// </summary>
internal static class FrameGenVulkan
{
    public const int StatusBytes = 648;

    public struct Status
    {
        public uint Enabled;
        public uint Loaded;
        public uint Ready;
        public uint Eligible;
        public uint Vsync;
        public uint RefreshHz;
        public uint Width;
        public uint Height;
        public uint GenerateCount;
        public uint SkipCount;
        public uint UsedVelocity;
        public uint LastFailed;
        public float GameFps;
        public float DisplayFps;
        public float MvScaleX;
        public float MvScaleY;
        public string Path;
        public string Error;
        public string Device;
        public string Evidence;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int StatusFn(IntPtr status);

    static StatusFn _status;
    static bool _missing;

    public static string StatusText()
    {
        if (!TryRead(out var status))
            return "Vulkan warp status is not available yet. The renderer export sevk_fg_status is missing.";
        return Format(status);
    }

    public static string OverlayText()
    {
        var config = Config.Current;
        if (config == null || !config.Enabled || !config.ShowOverlay)
            return "";
        if (!TryRead(out var status))
            return "FG  …";
        if (string.Equals(status.Path, "vsync", StringComparison.Ordinal))
            return "FG  " + status.GameFps.ToString("0") + " game · VSync";
        if (string.Equals(status.Path, "refresh-cap", StringComparison.Ordinal))
            return "FG  " + status.GameFps.ToString("0") + " game · " + status.RefreshHz + " Hz";
        return "FG  " + status.GameFps.ToString("0") + " game · " + status.DisplayFps.ToString("0") + " disp";
    }

    public static string Format(Status status)
    {
        var sb = new StringBuilder();
        var device = string.IsNullOrEmpty(status.Device) ? "Vulkan" : status.Device;
        sb.Append("GPU  ").Append(device);
        sb.Append(" · eligible ").AppendLine(Yes(status.Eligible != 0));
        sb.AppendLine();
        sb.Append("FG   ");
        if (status.Ready != 0)
            sb.Append("ready");
        else if (status.Loaded != 0 && status.Eligible != 0)
            sb.Append("loaded, not ready");
        else if (status.Loaded != 0)
            sb.Append("loaded, unsupported");
        else
            sb.Append("not loaded");
        sb.Append(" · ").AppendLine("FrameGen Vulkan warp");
        if (!string.IsNullOrEmpty(status.Error) && !(status.Ready != 0 && status.Error == "not initialized"))
            sb.Append("     ").AppendLine(status.Error);
        sb.AppendLine();
        sb.Append("On   ").AppendLine(Yes(status.Enabled != 0));
        sb.Append("Res  ").Append(status.Width).Append('x').AppendLine(status.Height.ToString());
        sb.Append("FPS  ").Append(status.GameFps.ToString("0"))
            .Append(" game · ").Append(status.DisplayFps.ToString("0")).Append(" displayed");
        if (status.RefreshHz >= 30)
            sb.Append(" · ").Append(status.RefreshHz).Append(" Hz");
        sb.AppendLine();
        sb.AppendLine();
        sb.Append("Gen  ");
        if (status.GenerateCount == 0 && status.SkipCount == 0)
            sb.AppendLine("none");
        else
        {
            sb.Append("interpolated ×").Append(status.GenerateCount);
            sb.Append(" · seed ×").Append(status.SkipCount);
            if (!string.IsNullOrEmpty(status.Path))
                sb.Append(" · ").Append(status.Path);
            sb.AppendLine();
        }
        sb.Append("     Anomaly velocity ").AppendLine(Yes(status.UsedVelocity != 0));
        if (status.LastFailed != 0)
            sb.AppendLine("     last interpolate failed; presenting real frames only");
        sb.AppendLine(string.IsNullOrEmpty(status.Evidence)
            ? "Motion vectors: no evaluate attempted"
            : status.Evidence);
        sb.AppendLine("AfterUpscale: unused (FrameGen is not an upscaler) · claim none · display no");
        return sb.ToString();
    }

    static bool TryRead(out Status status)
    {
        status = default;
        if (!TryResolve())
            return false;
        var buffer = Marshal.AllocHGlobal(StatusBytes);
        try
        {
            for (var i = 0; i < StatusBytes; i++)
                Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteInt32(buffer, 0, StatusBytes);
            Marshal.WriteInt32(buffer, 4, 1);
            if (_status(buffer) != 0)
                return false;
            status = new Status
            {
                Enabled = U32(buffer, 8),
                Loaded = U32(buffer, 12),
                Ready = U32(buffer, 16),
                Eligible = U32(buffer, 20),
                Vsync = U32(buffer, 24),
                RefreshHz = U32(buffer, 28),
                Width = U32(buffer, 32),
                Height = U32(buffer, 36),
                GenerateCount = U32(buffer, 40),
                SkipCount = U32(buffer, 44),
                UsedVelocity = U32(buffer, 48),
                LastFailed = U32(buffer, 52),
                GameFps = F32(buffer, 56),
                DisplayFps = F32(buffer, 60),
                MvScaleX = F32(buffer, 64),
                MvScaleY = F32(buffer, 68),
                Path = Text(buffer, 72, 64),
                Error = Text(buffer, 136, 192),
                Device = Text(buffer, 328, 128),
                Evidence = Text(buffer, 456, 192),
            };
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static uint U32(IntPtr buffer, int offset) => (uint)Marshal.ReadInt32(buffer, offset);

    static float F32(IntPtr buffer, int offset) => BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(buffer, offset)), 0);

    static string Text(IntPtr buffer, int offset, int bytes)
    {
        var copy = new byte[bytes];
        Marshal.Copy(IntPtr.Add(buffer, offset), copy, 0, bytes);
        var end = Array.IndexOf(copy, (byte)0);
        if (end < 0)
            end = copy.Length;
        return Encoding.UTF8.GetString(copy, 0, end);
    }

    static bool TryResolve()
    {
        if (_status != null)
            return true;
        if (_missing)
            return false;
        var module = Module();
        if (module == IntPtr.Zero)
        {
            // Status can be opened before the renderer loads its native module.
            // Only cache an absent export after the module is actually present.
            return false;
        }
        var exported = module == new IntPtr(1)
            ? Dlsym(IntPtr.Zero, "sevk_fg_status")
            : GetProcAddress(module, "sevk_fg_status");
        if (exported == IntPtr.Zero)
            exported = Dlsym(IntPtr.Zero, "sevk_fg_status");
        if (exported == IntPtr.Zero)
        {
            _missing = true;
            return false;
        }
        _status = Marshal.GetDelegateForFunctionPointer<StatusFn>(exported);
        return true;
    }

    static IntPtr Module()
    {
        try
        {
            var module = GetModuleHandle("se_vulkan_native.dll");
            if (module != IntPtr.Zero)
                return module;
        }
        catch
        {
            // kernel32 is absent on Linux. The loaded renderer exports the symbol.
        }

#if NET
        if (!OperatingSystem.IsWindows() && Dlsym(IntPtr.Zero, "sevk_fg_status") != IntPtr.Zero)
            return new IntPtr(1);
#endif
        return IntPtr.Zero;
    }

    static string Yes(bool value) => value ? "yes" : "no";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("libdl.so.2")]
    static extern IntPtr dlsym(IntPtr handle, string symbol);

    static IntPtr Dlsym(IntPtr handle, string symbol)
    {
        try { return dlsym(handle, symbol); }
        catch { return IntPtr.Zero; }
    }
}
