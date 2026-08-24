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
    private static readonly Regex VersionDirective = new(
        @"^[^\S\r\n]*#[^\S\r\n]*(?:if|elif)\b[^\r\n]*\b(?:JF10|JF12|NET9_0|NET10_0)\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void VersionSpecificCodeStaysInTheCompatFolder()
    {
        var projectRoot = PluginProjectRoot();
        Assert.True(
            Directory.Exists(projectRoot),
            $"Plugin sources not found at '{projectRoot}'. This test reads the checkout it was compiled from.");

        var compatRoot = Path.Combine(projectRoot, "Compat") + Path.DirectorySeparatorChar;

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.StartsWith(compatRoot, StringComparison.OrdinalIgnoreCase) || IsBuildOutput(file, projectRoot))
            {
                continue;
            }

            var relative = Path.GetRelativePath(projectRoot, file);
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

    private static bool IsBuildOutput(string file, string projectRoot)
    {
        var relative = Path.GetRelativePath(projectRoot, file);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static string PluginProjectRoot([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetDirectoryName(testsDirectory)!;
        return Path.Combine(repositoryRoot, "Jellyfin.Plugin.Streamyfin");
    }
}
