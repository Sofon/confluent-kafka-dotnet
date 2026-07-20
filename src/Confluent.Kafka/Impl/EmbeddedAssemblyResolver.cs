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

#if NETSTANDARD2_0 || NET462

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;


namespace Confluent.Kafka.Impl
{
    /// <summary>
    ///     Resolves the managed dependency assemblies (System.Memory etc.) that
    ///     have been embedded into this assembly as gzip compressed resources
    ///     (single-DLL builds, see the EmbedLibrdkafka MSBuild property in
    ///     Confluent.Kafka.csproj).
    ///
    ///     The resolver only kicks in when the runtime fails to locate an
    ///     assembly through regular means, so if the dependency is deployed next
    ///     to the application (or provided by the framework, as on .NET
    ///     Core/5+), that copy always wins. When the assembly contains no
    ///     embedded dependency resources (stock builds), nothing is hooked at all.
    /// </summary>
    internal static class EmbeddedAssemblyResolver
    {
        const string ResourcePrefix = "embedded.assembly.";

        static readonly object lockObj = new object();
        static readonly Dictionary<string, Assembly> loadedAssemblies
            = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register()
        {
            try
            {
                var names = typeof(EmbeddedAssemblyResolver).Assembly.GetManifestResourceNames();
                foreach (var name in names)
                {
                    if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                    {
                        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                        return;
                    }
                }
            }
            catch
            {
                // never throw from the module initializer.
            }
        }

        static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var name = new AssemblyName(args.Name).Name;
                lock (lockObj)
                {
                    if (loadedAssemblies.TryGetValue(name, out var cached))
                    {
                        return cached;
                    }

                    var assembly = typeof(EmbeddedAssemblyResolver).Assembly;
                    var stream = assembly.GetManifestResourceStream(ResourcePrefix + name + ".dll.gz");
                    if (stream == null)
                    {
                        return null;
                    }

                    using (stream)
                    using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                    using (var buffer = new MemoryStream())
                    {
                        gzip.CopyTo(buffer);
                        var result = Assembly.Load(buffer.ToArray());
                        loadedAssemblies[name] = result;
                        return result;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}


namespace System.Runtime.CompilerServices
{
    // Not defined in netstandard2.0 / net462; required for the [ModuleInitializer]
    // usage above (the compiler matches the attribute by its full name).
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

#endif
