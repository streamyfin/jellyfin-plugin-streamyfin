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
}
