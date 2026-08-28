using System;
using System.IO;
using System.Reflection;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace Confluent.Kafka.Impl
{
    internal static class BundledNativeBootstrap
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Initialize()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var is64 = IntPtr.Size == 8;
            var runtime = is64 ? "win-x64" : "win-x86";
            var resourcePrefix = "Confluent.Kafka.Bundled.Native." + runtime + ".";

            var baseUri = new Uri(assembly.GetName().EscapedCodeBase);
            var baseDirectory = Path.GetDirectoryName(baseUri.LocalPath);
            var nativeDirectory = Path.Combine(baseDirectory, "runtimes", runtime, "native");

            Directory.CreateDirectory(nativeDirectory);

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = resourceName.Substring(resourcePrefix.Length);
                var destination = Path.Combine(nativeDirectory, fileName);

                using (var input = assembly.GetManifestResourceStream(resourceName))
                {
                    if (input == null)
                    {
                        continue;
                    }

                    var shouldWrite = !File.Exists(destination) || new FileInfo(destination).Length != input.Length;
                    if (!shouldWrite)
                    {
                        continue;
                    }

                    var temporary = destination + ".tmp";
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }

                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }

                    File.Move(temporary, destination);
                }
            }
        }
    }
}
