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
    // A wait on a task, however it is spaced: Wait with or without a timeout, WaitAll and
    // WaitAny, Result, and GetAwaiter().GetResult(). WaitAsync is awaited, so it is not one.
    private static readonly Regex Blocks = new(
        @"\.\s*Wait(?:All|Any)?\s*\(|\.\s*Result\b|\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(",
        RegexOptions.Compiled);

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

    /// <summary>
    /// A wait is found however it is written: with a timeout, with a space before its
    /// parenthesis, or on several tasks at once. An await, an asynchronous wait and a name
    /// that only starts the same are not waits.
    /// </summary>
    /// <param name="line">A line of a route.</param>
    /// <param name="waits">Whether it waits on a task.</param>
    [Theory]
    [InlineData("task.Wait();", true)]
    [InlineData("task.Wait(1000);", true)]
    [InlineData("task.Wait (TimeSpan.FromSeconds(5));", true)]
    [InlineData("Task.WaitAll(first, second);", true)]
    [InlineData("Task.WaitAny(tasks);", true)]
    [InlineData("var sent = send.Result;", true)]
    [InlineData("send.GetAwaiter().GetResult();", true)]
    [InlineData("send.GetAwaiter ( ) . GetResult ( );", true)]
    [InlineData("await send;", false)]
    [InlineData("await send.WaitAsync(cancellation);", false)]
    [InlineData("var results = answer.Results;", false)]
    public void AWaitIsFoundHoweverItIsWritten(string line, bool waits) => Assert.Equal(waits, Blocks.IsMatch(line));
}
