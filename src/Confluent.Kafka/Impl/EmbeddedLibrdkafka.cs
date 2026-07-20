// Copyright 2026 Confluent Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Refer to LICENSE for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;


namespace Confluent.Kafka.Impl
{
    /// <summary>
    ///     Support for loading librdkafka native libraries that have been embedded
    ///     into this assembly as gzip compressed resources (single-DLL builds,
    ///     see the EmbedLibrdkafka MSBuild property in Confluent.Kafka.csproj).
    ///
    ///     The native libraries for the current platform are extracted to a
    ///     per-librdkafka-version cache directory (below the temp directory by
    ///     default, override with the CONFLUENT_KAFKA_LIBRDKAFKA_EXTRACT_DIR
    ///     environment variable) and loaded from there. When the assembly contains
    ///     no embedded native resources (stock builds), this class does nothing and
    ///     the regular librdkafka probing applies.
    /// </summary>
    internal static class EmbeddedLibrdkafka
    {
        const string ResourcePrefix = "librdkafka.";

#if !NET462
        static IntPtr preloadedHandle = IntPtr.Zero;
        static bool resolverRegistered = false;
#endif

        /// <summary>
        ///     Extract the embedded librdkafka binaries for the current platform
        ///     (if any) and attempt to load the main library.
        /// </summary>
        /// <param name="mainLibraryPath">
        ///     Path of the extracted main librdkafka library, or null.
        /// </param>
        /// <param name="preloaded">
        ///     true if the library was already loaded and a DllImport resolver
        ///     registered (in which case default DllImport binding will succeed and
        ///     mainLibraryPath does not need to be passed to the platform specific
        ///     loading routines).
        /// </param>
        /// <returns>
        ///     true if embedded binaries for the current platform were extracted.
        /// </returns>
        public static bool TryExtractAndPreload(out string mainLibraryPath, out bool preloaded)
        {
            mainLibraryPath = null;
            preloaded = false;

            try
            {
                var assembly = typeof(EmbeddedLibrdkafka).Assembly;

                var rid = GetRuntimeIdentifier();
                if (rid == null)
                {
                    return false;
                }

                var prefix = ResourcePrefix + rid + ".";
                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) &&
                                n.EndsWith(".gz", StringComparison.Ordinal))
                    .ToArray();
                if (resourceNames.Length == 0)
                {
                    return false;
                }

                var directory = GetExtractDirectory(assembly, rid);
                Directory.CreateDirectory(directory);
                foreach (var resourceName in resourceNames)
                {
                    var fileName = resourceName.Substring(
                        prefix.Length, resourceName.Length - prefix.Length - ".gz".Length);
                    ExtractResource(assembly, resourceName, Path.Combine(directory, fileName));
                }

                var candidates = GetMainLibraryCandidates(rid)
                    .Select(c => Path.Combine(directory, c))
                    .Where(File.Exists)
                    .ToArray();

                foreach (var candidatePath in candidates)
                {
                    if (TryPreload(candidatePath))
                    {
                        mainLibraryPath = candidatePath;
                        preloaded = true;
                        return true;
                    }
                }

                // NativeLibrary based preloading unavailable (e.g. .NET Framework):
                // hand the extracted path to the platform specific loading routines
                // in Librdkafka instead.
                mainLibraryPath = candidates.FirstOrDefault();
                return mainLibraryPath != null;
            }
            catch
            {
                // Never fail hard here: fall back to the default librdkafka
                // probing behaviour.
                return false;
            }
        }

        static string GetRuntimeIdentifier()
        {
#if NET462
            if (MonoSupport.IsMonoRuntime && Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                // Mono on non-Windows platforms: keep the default loading behaviour.
                return null;
            }
            return IntPtr.Size == 8 ? "win-x64" : "win-x86";
#else
            string arch;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64:
                    arch = "x64";
                    break;
                case Architecture.X86:
                    arch = "x86";
                    break;
                case Architecture.Arm64:
                    arch = "arm64";
                    break;
                default:
                    // covers architectures unknown to netstandard2.0, e.g. S390x.
                    arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                    break;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { return "win-" + arch; }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { return "osx-" + arch; }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return "linux-" + arch; }
            return null;
#endif
        }

        static string[] GetMainLibraryCandidates(string rid)
        {
            if (rid.StartsWith("win-", StringComparison.Ordinal))
            {
                return new[] { "librdkafka.dll" };
            }

            if (rid.StartsWith("osx-", StringComparison.Ordinal))
            {
                return new[] { "librdkafka.dylib" };
            }

            // linux: same variant preference as the delegate loading in Librdkafka.
            var osName = PlatformApis.GetOSName();
            if (osName != null && osName.Equals("alpine", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "alpine-librdkafka.so", "librdkafka.so", "centos8-librdkafka.so" };
            }
            return new[] { "librdkafka.so", "centos8-librdkafka.so", "alpine-librdkafka.so" };
        }

        static string GetExtractDirectory(Assembly assembly, string rid)
        {
            var root = Environment.GetEnvironmentVariable("CONFLUENT_KAFKA_LIBRDKAFKA_EXTRACT_DIR");
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Path.GetTempPath(), "confluent-kafka-dotnet");
            }

            var version = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == "LibrdkafkaRedistVersion")
                .Select(a => a.Value)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(version))
            {
                version = assembly.GetName().Version?.ToString() ?? "unknown";
            }

            return Path.Combine(root, "librdkafka-" + version, rid);
        }

        static void ExtractResource(Assembly assembly, string resourceName, string destination)
        {
            if (File.Exists(destination))
            {
                // extraction is atomic (write to temp file + move), so an existing
                // file is always complete. The directory name includes the
                // librdkafka version, so it is never stale.
                return;
            }

            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var resource = assembly.GetManifestResourceStream(resourceName))
                using (var gzip = new GZipStream(resource, CompressionMode.Decompress))
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
                {
                    gzip.CopyTo(output);
                }

                try
                {
                    File.Move(temp, destination);
                }
                catch (IOException)
                {
                    // lost the race against a concurrent extraction (other thread
                    // or process) - fine, as long as the file is there.
                    if (!File.Exists(destination))
                    {
                        throw;
                    }
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch (IOException) { }
                }
            }
        }

        static bool TryPreload(string libraryPath)
        {
#if NET462
            // The extracted path is passed to LoadNetFrameworkDelegates, which
            // loads it with LoadLibraryEx + LOAD_WITH_ALTERED_SEARCH_PATH (the
            // Windows loader then satisfies DllImport("librdkafka") from the
            // already loaded module).
            return false;
#elif NET6_0_OR_GREATER
            try
            {
                var handle = NativeLibrary.Load(libraryPath);
                preloadedHandle = handle;
                if (!resolverRegistered)
                {
                    NativeLibrary.SetDllImportResolver(typeof(EmbeddedLibrdkafka).Assembly, ResolveDllImport);
                    resolverRegistered = true;
                }
                return true;
            }
            catch (InvalidOperationException)
            {
                // a resolver was already registered for this assembly.
                resolverRegistered = true;
                return true;
            }
            catch
            {
                return false;
            }
#else
            // netstandard2.0: System.Runtime.InteropServices.NativeLibrary is
            // available on .NET Core 3.0+ only - use it via reflection. On .NET
            // Framework this returns false and the extracted path is passed to
            // the LoadLibraryEx based loading instead.
            try
            {
                var nativeLibrary = Type.GetType(
                    "System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices", false);
                var resolverType = Type.GetType(
                    "System.Runtime.InteropServices.DllImportResolver, System.Runtime.InteropServices", false);
                if (nativeLibrary == null || resolverType == null)
                {
                    return false;
                }

                var load = nativeLibrary.GetMethod("Load", new[] { typeof(string) });
                var setResolver = nativeLibrary.GetMethod(
                    "SetDllImportResolver", new[] { typeof(Assembly), resolverType });
                if (load == null || setResolver == null)
                {
                    return false;
                }

                IntPtr handle;
                try
                {
                    handle = (IntPtr)load.Invoke(null, new object[] { libraryPath });
                }
                catch (TargetInvocationException)
                {
                    // the library failed to load (e.g. musl variant on glibc);
                    // let the caller try the next candidate.
                    return false;
                }

                preloadedHandle = handle;
                if (!resolverRegistered)
                {
                    var resolveMethod = typeof(EmbeddedLibrdkafka).GetMethod(
                        nameof(ResolveDllImport), BindingFlags.NonPublic | BindingFlags.Static);
                    var resolver = Delegate.CreateDelegate(resolverType, resolveMethod);
                    try
                    {
                        setResolver.Invoke(null, new object[] { typeof(EmbeddedLibrdkafka).Assembly, resolver });
                    }
                    catch (TargetInvocationException)
                    {
                        // a resolver was already registered for this assembly.
                    }
                    resolverRegistered = true;
                }
                return true;
            }
            catch
            {
                return false;
            }
#endif
        }

#if !NET462
        static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (preloadedHandle != IntPtr.Zero &&
                libraryName != null &&
                libraryName.IndexOf("librdkafka", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return preloadedHandle;
            }
            return IntPtr.Zero;
        }
#endif
    }
}
