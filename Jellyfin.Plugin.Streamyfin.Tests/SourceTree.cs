using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The checkout the tests were compiled from, for the tests that read the sources
/// rather than run them.
/// </summary>
internal static class SourceTree
{
    /// <summary>
    /// The repository root, failing the calling test with a reason when the sources
    /// are not there, which happens when the built tests are moved elsewhere.
    /// </summary>
    public static string Root()
    {
        var root = RootOf();
        Assert.True(
            Directory.Exists(root),
            $"Sources not found at '{root}'. This test reads the checkout it was compiled from.");
        return root;
    }

    /// <summary>
    /// Every C# source under <paramref name="folder"/>, build output left out.
    /// </summary>
    /// <param name="folder">A folder under the repository root.</param>
    public static IEnumerable<string> CSharpFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file, Root()));

    /// <summary>
    /// Whether <paramref name="file"/> sits in a bin or obj folder of the checkout. The
    /// path is taken relative to the root, so a checkout that itself lives under a
    /// folder named bin or obj still counts its sources.
    /// </summary>
    public static bool IsBuildOutput(string file, string root)
    {
        var relative = Path.GetRelativePath(root, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    // This file sits in the tests folder, one level below the root.
    private static string RootOf([CallerFilePath] string thisFile = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(thisFile)!)!;
}
