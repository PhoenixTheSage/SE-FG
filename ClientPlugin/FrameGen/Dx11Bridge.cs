using System;
using System.IO;
using System.Reflection;

namespace ClientPlugin.FrameGen;

/// <summary>
/// Loads SpaceEngineersFrameGen.Dx11.dll only when the DX11 device is the renderer.
/// The main assembly does not reference SharpDX or VRage.Render11.
/// </summary>
static class Dx11Bridge
{
    static Type entry;
    static bool failed;
    static string lastError;
    static readonly object Gate = new();

    public static string StatusText => Call<string>("StatusText") ??
        "FrameGen DX11 is unavailable.\n" + (lastError ?? "Waiting for the DX11 companion.") +
        "\nRenderer: " + AnomalyRenderer.ActiveName;

    public static void Ensure()
    {
        lock (Gate)
            EnsureLocked();
    }

    static void EnsureLocked()
    {
        if (entry != null || failed || !AnomalyRenderer.AllowDx11Device)
            return;
        string path = Resolve("SpaceEngineersFrameGen.Dx11.dll");
        if (!File.Exists(path))
        {
            var message = "DX11 satellite missing at " + path;
            if (lastError != message)
                VRage.Utils.MyLog.Default.WriteLine("FrameGen: " + message);
            lastError = message;
            return;
        }
        try
        {
            entry = SatelliteAssembly.Load(path, typeof(Dx11Bridge).Assembly).GetType("ClientPlugin.FrameGen.Dx11Entry", true);
            lastError = null;
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: DX11 satellite " + path);
        }
        catch (Exception e)
        {
            lastError = "DX11 satellite failed at " + path + ": " + e.GetBaseException();
            failed = true;
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: " + lastError);
        }
    }

    static string Resolve(string fileName)
    {
        string dir = Path.GetDirectoryName(typeof(Dx11Bridge).Assembly.Location) ?? "";
        string beside = Path.Combine(dir, fileName);
        if (File.Exists(beside))
            return beside;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type lookup;
            try { lookup = assembly.GetType("ClientPlugin.Backend.SatelliteLookup", false); }
            catch { continue; }
            var found = lookup?.GetMethod("Find")?.Invoke(null, new object[] { fileName, typeof(Dx11Bridge).Assembly }) as string;
            if (!string.IsNullOrEmpty(found) && File.Exists(found))
                return found;
        }
        return beside;
    }

    public static bool Ready
    {
        get
        {
            Ensure();
            return AnomalyRenderer.AllowDx11Device && entry != null && !failed;
        }
    }

    public static void Call(string name, params object[] args)
    {
        Invoke(name, args);
    }

    public static T Call<T>(string name, params object[] args)
    {
        object value = Invoke(name, args);
        return value is T typed ? typed : default;
    }

    static object Invoke(string name, object[] args)
    {
        if (failed || !AnomalyRenderer.AllowDx11Device)
            return null;
        Ensure();
        try
        {
            if (entry == null)
                return null;
            var method = entry.GetMethod(name) ?? throw new MissingMethodException(entry.FullName, name);
            return method.Invoke(null, args == null || args.Length == 0 ? null : args);
        }
        catch (Exception e)
        {
            failed = true;
            var root = e.GetBaseException();
            lastError = "DX11 " + name + " failed: " + root;
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: DX11 " + name +
                                            " failed; satellite disabled this session: " +
                                            root.GetType().Name + ": " + root.Message);
            return null;
        }
    }
}
