using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// AGPL-3.0 or a commercial license, while the project referenced 3.0.16 (#244). The Makefile
/// zips the folder whole and flat, so a DLL in a folder of it ships too.
/// </remarks>
public class PackagedLibrariesTests
{
    /// <summary>
    /// What one DLL in <c>packages/</c> is, read from its metadata without loading it.
    /// </summary>
    /// <param name="Path">The file, for the messages.</param>
    /// <param name="Name">The assembly's name, or <c>null</c> when the file is not a .NET assembly.</param>
    /// <param name="Version">The assembly's version, or <c>null</c> with the name.</param>
    /// <param name="References">The assemblies it references, by name and version.</param>
    private sealed record Library(string Path, string? Name, Version? Version, IReadOnlyList<(string Name, Version Version)> References);

    /// <summary>
    /// Reads an assembly file's name, version and references from its metadata, which a native
    /// library has none of, and a file that is no Windows image at all has no headers to say so.
    /// </summary>
    /// <param name="dll">The file.</param>
    /// <returns>What it is.</returns>
    private static Library Read(string dll)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        bool managed;
        try
        {
            managed = pe.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            managed = false;
        }

        if (!managed)
        {
            return new Library(dll, null, null, []);
        }

        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var references = metadata.AssemblyReferences
            .Select(handle => metadata.GetAssemblyReference(handle))
            .Select(reference => (metadata.GetString(reference.Name), reference.Version))
            .ToList();
        return new Library(dll, metadata.GetString(definition.Name), definition.Version, references);
    }

    /// <summary>
    /// Every DLL the zip takes from <c>packages/</c>, in a folder of it too, whatever the case
    /// of its extension.
    /// </summary>
    /// <returns>What each one is.</returns>
    private static List<Library> Packaged() => Directory
        .EnumerateFiles(Path.Combine(SourceTree.Root(), "packages"), "*", SearchOption.AllDirectories)
        .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(Read)
        .ToList();

    /// <summary>
    /// What the plugin's own assembly references.
    /// </summary>
    /// <returns>The plugin as a library.</returns>
    private static Library Plugin() => Read(typeof(StreamyfinPlugin).Assembly.Location);

    /// <summary>
    /// Every DLL the zip ships from <c>packages/</c> is a .NET assembly: of a native one, nothing
    /// here can show that the plugin uses it.
    /// </summary>
    [Fact]
    public void EveryLibraryTheZipShipsIsADotNetAssembly()
    {
        var native = Packaged().Where(library => library.Name is null).Select(library => Path.GetFileName(library.Path)).ToList();

        Assert.True(native.Count == 0, "Not .NET assemblies: " + string.Join(", ", native));
    }

    /// <summary>
    /// Every library the zip ships is one the plugin uses, itself or through another of them.
    /// </summary>
    [Fact]
    public void EveryLibraryTheZipShipsIsOneThePluginUses()
    {
        var packaged = Packaged().Where(library => library.Name is not null)
            .ToDictionary(library => library.Name!, StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = new Queue<string>(Plugin().References.Select(reference => reference.Name));
        while (next.TryDequeue(out var name))
        {
            if (packaged.TryGetValue(name, out var library) && used.Add(name))
            {
                foreach (var reference in library.References)
                {
                    next.Enqueue(reference.Name);
                }
            }
        }

        var unused = packaged.Keys.Where(name => !used.Contains(name)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unused.Count == 0, "Shipped and used by nothing: " + string.Join(", ", unused));
    }

    /// <summary>
    /// No library the zip ships is older than the version the plugin, or another library it
    /// ships, was built against: the project's reference moved on and <c>packages/</c> did not,
    /// as it did for Json.NET Schema, and the plugin's load context refuses an assembly older
    /// than the one asked for at its first use.
    /// </summary>
    [Fact]
    public void NoLibraryTheZipShipsIsOlderThanTheOneReferenced()
    {
        var packaged = Packaged().Where(library => library.Name is not null)
            .ToDictionary(library => library.Name!, StringComparer.OrdinalIgnoreCase);

        var older = packaged.Values.Prepend(Plugin())
            .SelectMany(library => library.References.Select(reference => (By: library, Reference: reference)))
            .Where(one => packaged.TryGetValue(one.Reference.Name, out var shipped) && shipped.Version < one.Reference.Version)
            .Select(one => $"{one.Reference.Name} {packaged[one.Reference.Name].Version} shipped, {one.Reference.Version} referenced by {one.By.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(older.Count == 0, "Older than referenced: " + string.Join("; ", older));
    }
}
