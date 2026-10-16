using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A route that waits on a task holds one of the server's threads for as long as the task
/// takes, and an exception reaches the middleware wrapped in an <c>AggregateException</c>.
/// </summary>
/// <remarks>
/// The notification route waited on its send to Expo, retries included, so a few calls to it
/// while Expo answered slowly held threads the whole server shares (#246). The routes cannot
/// be built in a test, so the sources are read, as <see cref="ControllerLogTests"/> does.
/// </remarks>
public class RoutesAwaitTests
{
    // A wait on a task: Wait(), Result, or GetAwaiter().GetResult().
    private static readonly Regex Blocks = new(@"\.Wait\(\)|\.Result\b|\.GetAwaiter\(\)\.GetResult\(\)", RegexOptions.Compiled);

    /// <summary>
    /// No route in the plugin's API waits on a task.
    /// </summary>
    [Fact]
    public void NoRouteWaitsOnATask()
    {
        var api = Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin", "Api");

        var waits = SourceTree.CSharpFiles(api)
            .SelectMany(file => File.ReadAllLines(file).Select((line, at) => (File: file, Line: line, At: at + 1)))
            .Where(one => !one.Line.TrimStart().StartsWith("//", StringComparison.Ordinal) && Blocks.IsMatch(one.Line))
            .Select(one => $"{Path.GetFileName(one.File)}:{one.At}: {one.Line.Trim()}")
            .ToList();

        Assert.True(waits.Count == 0, "Waits on a task: " + string.Join(", ", waits));
    }
}
