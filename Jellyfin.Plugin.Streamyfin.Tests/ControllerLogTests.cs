using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What an administrator sends, written into the server's log by the routes.
/// </summary>
/// <remarks>
/// A group's name comes from the request, and the plugin a backup says it was taken by comes
/// from the uploaded file. A line break or an escape sequence in either writes an entry that
/// reads as the server's. CodeQL finds the first kind once a change reaches a branch it scans;
/// this finds both before it is merged. The routes cannot be built in a test, so the source is
/// read, as <see cref="CompatBoundaryTests"/> does.
/// </remarks>
public class ControllerLogTests
{
    // A logger call, from its opening parenthesis to the one that closes it.
    private static readonly Regex LogCall = new(
        @"_logger\.Log\w+\((?<arguments>.*?)\);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // A value read off a request body or a restored backup, with the call made on it if any.
    private static readonly Regex Sent = new(
        @"\b(?:request|backup)\.\w+(?:\.\w+\(\))?",
        RegexOptions.Compiled);

    // Values that are not text, so nothing in them can start a line.
    private static readonly string[] NotText = ["backup.TakenAt"];

    [Fact]
    public void WhatARequestSendsIsLoggedOnOneLine()
    {
        var controller = Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin", "Api", "StreamyfinController.cs");

        var raw = LogCall.Matches(File.ReadAllText(controller))
            .SelectMany(call => Sent.Matches(call.Groups["arguments"].Value))
            .Select(value => value.Value)
            .Where(value => !value.EndsWith(".ForLog()", StringComparison.Ordinal) && !NotText.Contains(value))
            .ToList();

        Assert.True(raw.Count == 0, "Logged as sent, without ForLog(): " + string.Join(", ", raw));
    }
}
