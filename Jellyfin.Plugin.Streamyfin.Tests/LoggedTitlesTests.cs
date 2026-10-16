using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The titles the notifications name, written into the server's log.
/// </summary>
/// <remarks>
/// A title comes from a file name or an <c>.nfo</c>, and was logged through
/// <c>Escape()</c>, which escapes quotes and nothing else: a title with a line break wrote an
/// entry of its own (#277). <c>ForLog()</c> cleans it. <c>Escape()</c> stays for the texts of
/// the notifications themselves.
/// </remarks>
public class LoggedTitlesTests
{
    // A logger call, from its opening parenthesis to the one that closes it.
    private static readonly Regex LogCall = new(
        @"_logger\.Log\w+\((?<arguments>.*?)\);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// No logger call in the plugin passes a value through <c>Escape()</c>.
    /// </summary>
    [Fact]
    public void NoLoggerCallTakesEscapeForCleaning()
    {
        var plugin = Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin");

        var escaped = SourceTree.CSharpFiles(plugin)
            .SelectMany(file => LogCall.Matches(File.ReadAllText(file))
                .Where(call => call.Groups["arguments"].Value.Contains(".Escape()", StringComparison.Ordinal))
                .Select(call => $"{Path.GetFileName(file)}: {call.Value.Split('\n')[0].Trim()}"))
            .ToList();

        Assert.True(escaped.Count == 0, "Logged through Escape(), which leaves line breaks in: " + string.Join(", ", escaped));
    }

    // A string literal, so the words of a message template are not taken for names.
    private static readonly Regex Literal = new(@"""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

    // A name passed as it is, neither a member of something nor a call.
    private static readonly Regex BareName = new(@"(?<![\w.])(?<name>[a-z_]\w*)\b(?![\w.(])", RegexOptions.Compiled);

    /// <summary>
    /// A title put through <c>Escape()</c> into a local, and the local logged, is the same
    /// line break in the log: the series name the episodes timer logs went that way. Each
    /// name a logger call passes is traced to the last assignment before the call.
    /// </summary>
    [Fact]
    public void NoLoggerCallTakesALocalThatEscapeWrote()
    {
        var plugin = Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin");
        var escaped = new System.Collections.Generic.List<string>();

        foreach (var file in SourceTree.CSharpFiles(plugin))
        {
            var source = File.ReadAllText(file);

            foreach (Match call in LogCall.Matches(source))
            {
                var arguments = Literal.Replace(call.Groups["arguments"].Value, "\"\"");

                foreach (var name in BareName.Matches(arguments).Select(found => found.Groups["name"].Value).Distinct())
                {
                    var assignment = Regex.Matches(source[..call.Index], $@"\b(?:var\s+)?{name}\s*=(?!=)\s*(?<value>[^;]*);")
                        .LastOrDefault();

                    if (assignment is not null && assignment.Groups["value"].Value.Contains(".Escape()", StringComparison.Ordinal))
                    {
                        escaped.Add($"{Path.GetFileName(file)}: {name} = {assignment.Groups["value"].Value.Trim()}");
                    }
                }
            }
        }

        Assert.True(escaped.Count == 0, "Logged from a local Escape() wrote, which leaves line breaks in: " + string.Join(", ", escaped));
    }
}
