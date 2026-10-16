using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The libraries the zip ships beside the plugin, from <c>packages/</c>.
/// </summary>
/// <remarks>
/// <c>packages/</c> is committed by hand, and Jellyfin loads every DLL in the plugin's folder.
/// It shipped Json.NET Schema 4.0.1, which nothing in the plugin calls, published under the
/// AGPL-3.0 or a commercial license, while the project referenced 3.0.16 (#244).
/// </remarks>
public class PackagedLibrariesTests
{
    /// <summary>
    /// The name an assembly file declares, which is the name another assembly references it by.
    /// </summary>
    /// <param name="dll">The assembly file.</param>
    /// <returns>Its name.</returns>
    private static string? NameOf(string dll) => AssemblyName.GetAssemblyName(dll).Name;

    /// <summary>
    /// The names of the assemblies an assembly file references, read from its metadata without
    /// loading it.
    /// </summary>
    /// <param name="dll">The assembly file.</param>
    /// <returns>The names it references.</returns>
    private static IEnumerable<string> ReferencesOf(string dll)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToList();
    }

    /// <summary>
    /// Every library the zip ships is one the plugin uses, itself or through another of them.
    /// </summary>
    [Fact]
    public void EveryLibraryTheZipShipsIsOneThePluginUses()
    {
        var packaged = Directory.EnumerateFiles(Path.Combine(SourceTree.Root(), "packages"), "*.dll")
            .ToDictionary(dll => NameOf(dll) ?? dll, dll => dll, StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = new Queue<string>(ReferencesOf(typeof(StreamyfinPlugin).Assembly.Location));
        while (next.TryDequeue(out var name))
        {
            if (packaged.TryGetValue(name, out var dll) && used.Add(name))
            {
                foreach (var reference in ReferencesOf(dll))
                {
                    next.Enqueue(reference);
                }
            }
        }

        var unused = packaged.Keys.Where(name => !used.Contains(name)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unused.Count == 0, "Shipped and used by nothing: " + string.Join(", ", unused));
    }
}
