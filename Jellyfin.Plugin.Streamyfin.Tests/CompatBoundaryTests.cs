using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Guards the rule stated in Compat/README.md: version specific code lives in
/// the Compat folder and nowhere else, so that retiring a Jellyfin version is a
/// deletion rather than a hunt through the whole codebase.
/// </summary>
public class CompatBoundaryTests
{
    // Matches NET<n>_0 rather than the two current monikers, and spells out
    // _OR_GREATER: a word boundary after NET10_0 does not stop at
    // NET10_0_OR_GREATER, since the underscore is a word character, so that
    // spelling slipped straight through.
    private static readonly Regex VersionDirective = new(
        @"^[^\S\r\n]*#[^\S\r\n]*(?:if|elif)\b[^\r\n]*\b(?:JF11|JF12|NET\d+_0(?:_OR_GREATER)?)\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void VersionSpecificCodeStaysInTheCompatFolder()
    {
        var repositoryRoot = RepositoryRoot();
        Assert.True(
            Directory.Exists(repositoryRoot),
            $"Sources not found at '{repositoryRoot}'. This test reads the checkout it was compiled from.");

        var compatRoot = Path.Combine(repositoryRoot, "Jellyfin.Plugin.Streamyfin", "Compat") + Path.DirectorySeparatorChar;

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.StartsWith(compatRoot, StringComparison.OrdinalIgnoreCase) || IsBuildOutput(file, repositoryRoot))
            {
                continue;
            }

            var relative = Path.GetRelativePath(repositoryRoot, file);
            offenders.AddRange(
                VersionDirective.Matches(File.ReadAllText(file))
                    .Select(match => $"{relative}: {match.Value.Trim()}"));
        }

        Assert.True(
            offenders.Count == 0,
            "Version specific preprocessor directives found outside Compat/. Move the branch into Compat "
            + "and expose it as a normal API. See Compat/README.md."
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    private static bool IsBuildOutput(string file, string repositoryRoot)
    {
        var relative = Path.GetRelativePath(repositoryRoot, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetDirectoryName(testsDirectory)!;
    }
}
