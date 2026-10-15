using System;
using System.Text;
using System.Threading;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Yaml documents that set nothing an administrator could want, and instead hold
/// themselves, multiply through aliases or nest without end. Each is refused, quickly. None
/// may overflow the stack: that ends the server's process, which no catch can stop.
/// </summary>
public class HostileYamlTests
{
    private readonly SerializationHelper _serialization = new();

    /// <summary>
    /// What saving the document answers, decided on a thread with a 1 MiB stack, about what
    /// a request gets, and within ten seconds.
    /// </summary>
    private string? Decide(string yaml)
    {
        string? problem = null;
        var thread = new Thread(
            () => problem = StreamyfinController.ToSave(_serialization, yaml, new PluginConfiguration().Config.settings).Problem,
            1024 * 1024);

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "still deciding after ten seconds");
        return problem;
    }

    /// <summary>
    /// A node that holds itself through its own anchor. The reader hands back the very node
    /// an alias names, so a walk down such a document never reaches its end. It is left to
    /// the deserializer, which says why it cannot read it, rather than called empty.
    /// </summary>
    /// <param name="yaml">What the editor sent.</param>
    [Theory]
    [InlineData("settings: &loop\n  next: *loop\n")]
    [InlineData("settings:\n  forwardSkipTime: &loop\n    value: *loop\n")]
    [InlineData("settings: &loop [*loop]\n")]
    public void ANodeThatHoldsItselfIsRefused(string yaml)
    {
        var problem = Decide(yaml);

        Assert.NotNull(problem);
        Assert.NotEqual(StreamyfinController.EmptyYaml, problem);
    }

    /// <summary>
    /// A key that holds itself. Loading it into the reader's own tree hashes the key, and
    /// that hash goes round the key forever.
    /// </summary>
    [Fact]
    public void AKeyThatHoldsItselfIsRefused()
    {
        Assert.NotNull(Decide("? &key\n  ? *key\n  : 1\n: 2\n"));
    }

    /// <summary>
    /// Forty mappings, each naming the one before it twice, with nothing at the bottom: a
    /// walk that follows every alias meets the bottom 2^40 times.
    /// </summary>
    [Fact]
    public void AliasesThatMultiplyAreRefusedQuickly()
    {
        var yaml = new StringBuilder("settings:\n  l0: &l0 {a: ~, b: ~}\n");
        for (var level = 1; level <= 40; level++)
        {
            yaml.Append(System.Globalization.CultureInfo.InvariantCulture, $"  l{level}: &l{level} {{a: *l{level - 1}, b: *l{level - 1}}}\n");
        }

        Assert.NotNull(Decide(yaml.ToString()));
    }

    /// <summary>
    /// Lists inside lists, far deeper than any configuration goes.
    /// </summary>
    /// <param name="depth">How many lists deep.</param>
    [Theory]
    [InlineData(10_000)]
    [InlineData(200_000)]
    public void NestingDeeperThanAnyConfigurationIsRefused(int depth)
    {
        Assert.NotNull(Decide("settings: " + new string('[', depth) + new string(']', depth) + "\n"));
    }
}
