using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A wait on a task holds one of the threads the whole server shares for as long as the task
/// takes: the notification route waited on its send to Expo, retries included, so a few calls
/// to it while Expo answered slowly held the server's threads (#246).
/// </summary>
/// <remarks>
/// The build refuses every such wait in the plugin, through <c>BannedSymbols.txt</c> (RS0030):
/// a check on the symbol, so a wait written over several lines is refused as well, and a method
/// of the plugin that is only named Wait is not. This holds the list to every way of waiting,
/// since an entry without its parameters names only the overload that has none.
/// </remarks>
public class RoutesAwaitTests
{
    /// <summary>
    /// The banned list names every overload of every way to wait on a task.
    /// </summary>
    /// <param name="symbol">The documentation id of one way to wait.</param>
    [Theory]
    [InlineData("M:System.Threading.Tasks.Task.Wait")]
    [InlineData("M:System.Threading.Tasks.Task.Wait(System.Int32)")]
    [InlineData("M:System.Threading.Tasks.Task.Wait(System.TimeSpan)")]
    [InlineData("M:System.Threading.Tasks.Task.Wait(System.Threading.CancellationToken)")]
    [InlineData("M:System.Threading.Tasks.Task.Wait(System.Int32,System.Threading.CancellationToken)")]
    [InlineData("M:System.Threading.Tasks.Task.Wait(System.TimeSpan,System.Threading.CancellationToken)")]
    [InlineData("M:System.Threading.Tasks.Task.WaitAll(System.Threading.Tasks.Task[])")]
    [InlineData("M:System.Threading.Tasks.Task.WaitAny(System.Threading.Tasks.Task[])")]
    [InlineData("P:System.Threading.Tasks.Task`1.Result")]
    [InlineData("P:System.Threading.Tasks.ValueTask`1.Result")]
    [InlineData("M:System.Runtime.CompilerServices.TaskAwaiter.GetResult")]
    [InlineData("M:System.Runtime.CompilerServices.TaskAwaiter`1.GetResult")]
    [InlineData("M:System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter.GetResult")]
    [InlineData("M:System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1.ConfiguredTaskAwaiter.GetResult")]
    public void TheBuildRefusesAWaitOnATask(string symbol)
    {
        Assert.Contains(symbol, SourceTree.BannedSymbols());
    }
}
