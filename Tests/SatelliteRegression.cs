using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

// Exercises actual built plugins without starting the game or creating a GPU device.
internal static class SatelliteRegression
{
    static int Main(string[] args)
    {
        string directory = Path.GetFullPath(args[0]), game = Path.GetFullPath(args[1]), name = args[2];
        bool sources = args.Length > 3 && args[3] == "sources";
        string scratch = Path.Combine(Path.GetTempPath(), "SeSatellite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
                foreach (string root in new[] { directory, game, AppDomain.CurrentDomain.BaseDirectory })
                {
                    string path = Path.Combine(root, new AssemblyName(e.Name).Name + ".dll");
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            string corePath = Path.Combine(directory, name + ".dll");
            string devicePath = Path.Combine(directory, name + ".Dx11.dll");
            using (var resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(directory); resolver.AddSearchDirectory(game);
                foreach (string path in new[] { corePath, devicePath })
                    using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver }))
                    {
                        Check(!module.Assembly.CustomAttributes.Any(a => a.AttributeType.Name == "IgnoresAccessChecksToAttribute" &&
                            string.IsNullOrWhiteSpace(a.ConstructorArguments[0].Value as string)), Path.GetFileName(path) + " has no empty access grant");
                        if (path == corePath)
                        {
                            Check(!module.AssemblyReferences.Any(a => a.Name == "VRage.Render11" || a.Name.StartsWith("SharpDX")),
                                "shared plugin has no device references");
                            if (sources)
                            {
                                module.Assembly.Name.Name = name + ".PulsarRegression";
                                corePath = Path.Combine(scratch, "plugin.dll"); module.Write(corePath);
                            }
                        }
                    }
            }
            var core = Assembly.LoadFrom(corePath);
            string prefix = name == "SpaceEngineersFrameGen" ? "ClientPlugin.FrameGen." :
                            name == "HdrOutput" ? "ClientPlugin.Anomaly." : "ClientPlugin.";
            var loader = core.GetType(prefix + "SatelliteAssembly", true);
            var bridge = core.GetType(prefix + "Dx11Bridge", false);
            Action closedCall;
            if (bridge != null)
            {
                var call = bridge.GetMethods().Single(m => m.Name == "Call" && !m.IsGenericMethod);
                closedCall = () => call.Invoke(null, new object[] { "OnUpdate", new object[0] });
            }
            else if (name == "Prism.SSGI")
                closedCall = () => core.GetType("ClientPlugin.SsgiDx11Host", true).GetMethod("Reload").Invoke(null, null);
            else
                closedCall = () => core.GetType("ClientPlugin.SoftwareBackend", true).GetMethod("TryCreate").Invoke(null, new object[] { null, null });
            foreach (int route in new[] { 0, 2 })
            {
                ClientPlugin.Backend.RendererRoute.Active = route; closedCall();
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name + ".Dx11"),
                    "route " + route + " keeps the device companion unloaded");
                if (bridge != null)
                    Check(!string.IsNullOrWhiteSpace((string)bridge.GetProperty("StatusText").GetValue(null)), "closed bridge reports readable status");
            }
            ClientPlugin.Backend.RendererRoute.Active = 1;
            var device = (Assembly)loader.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { devicePath, core });
            Check(device.GetReferencedAssemblies().Any(a => a.FullName == core.FullName), "companion references the live core identity");
            if (name == "SpaceEngineersFrameGen")
            {
                var entry = device.GetType(prefix + "Dx11Entry", true);
                entry.GetField("failed", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
                entry.GetField("initializationError", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, "fixture startup failure");
                Check(((string)entry.GetMethod("StatusText").Invoke(null, null)).Contains("fixture startup failure"), "startup failure remains visible in Show Status");
                device.GetType(prefix + "AnomalyHook", true).GetMethod("ProbeBody", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                Check(true, "FrameGen discovery executes across internal access checks");
                bridge.GetField("entry", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, entry);
                ClientPlugin.Backend.RendererRoute.Active = 2; closedCall();
                Check(!(bool)entry.GetField("routed", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "Vulkan cannot invoke an already loaded device companion");
            }
            else if (name == "HdrOutput")
            {
                device.GetType(prefix + "Dx11Entry", true).GetMethod("ApplyAssets").Invoke(null, null);
                Check(true, "HDR asset callbacks access the live plugin");
            }
            else if (name == "Prism.SSGI")
            {
                var config = core.GetType("ClientPlugin.Config.SSGIConfig", true);
                Check(device.GetType("ClientPlugin.SSGI.SSGIPass", true).GetMethod("TraceScale", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null) is float,
                    "Prism pass reads live plugin settings without a duplicate core");
            }
            else
            {
                var contract = core.GetType("SERT.Rendering.IRayTracingBackend", true);
                var implementation = device.GetType("SERT.Rendering.SoftwareDx11", true);
                Check(contract.IsAssignableFrom(implementation), "ray tracing backend implements the live core interface");
                Check(implementation.GetConstructors().Single().GetParameters()[0].ParameterType.Assembly == core,
                    "ray tracing constructor accepts the live host type");
                var backend = Activator.CreateInstance(implementation, new object[] { null, (Func<string>)(() => scratch), false });
                Check(contract.GetProperty("Status").GetValue(backend) as string == "idle", "ray tracing backend constructs without an incompatible type identity");
            }
            Check(!sources || !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name), "source mode never loads a second disk core");
            loader.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Console.WriteLine("PASS " + name + " / " + (sources ? "sources" : "local"));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(scratch).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
}
namespace ClientPlugin.Backend
{
    public static class RendererRoute
    {
        public static int Active { get; set; }
        public static string ActiveName => Active == 1 ? "DX11" : Active == 2 ? "Vulkan" : "pending";
        public static bool Decided => Active != 0;
        public static bool UseDx11Device => Active == 1;
        public static bool UseGuestImages => Active == 2;
    }
}
