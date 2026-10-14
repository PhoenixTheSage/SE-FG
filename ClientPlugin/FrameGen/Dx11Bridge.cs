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

    public static void Ensure()
    {
        if (entry != null || !AnomalyRenderer.AllowDx11Device)
            return;
        string path = Resolve("SpaceEngineersFrameGen.Dx11.dll");
        if (!File.Exists(path))
        {
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: DX11 satellite missing at " + path);
            return;
        }
        try
        {
            entry = Assembly.LoadFrom(path).GetType("ClientPlugin.FrameGen.Dx11Entry");
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: DX11 satellite " + path);
        }
        catch (Exception e)
        {
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: DX11 satellite failed at " + path + ": " + e.GetType().Name);
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
        if (failed)
            return null;
        Ensure();
        try
        {
            return entry?.GetMethod(name)?.Invoke(null, args == null || args.Length == 0 ? null : args);
        }
        catch (Exception e)
        {
            failed = true;
            var root = e.GetBaseException();
            VRage.Utils.MyLog.Default.WriteLine("FrameGen: DX11 " + name +
                                            " failed; satellite disabled this session: " +
                                            root.GetType().Name + ": " + root.Message);
            return null;
        }
    }
}
