using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A key written twice in one mapping of a Yaml document.
/// </summary>
/// <remarks>
/// Yaml does not allow it, and the deserializer kept the last value and dropped the first
/// without a word. The editor sends the whole configuration, so a second block pasted under
/// the first quietly undid what the first one set (#266).
/// </remarks>
public class YamlKeysTests
{
    private readonly SerializationHelper _serialization = new();

    private string? Problem(string yaml) =>
        StreamyfinController.ToSave(_serialization, yaml, new PluginConfiguration().Config.settings).Problem;

    /// <summary>
    /// The document from the issue: a second <c>settings</c> block after <c>other</c>. It was
    /// stored with the rewind time alone, the forward skip time of the first block gone. The
    /// sentence names the key and where the second one is.
    /// </summary>
    [Fact]
    public void ABlockWrittenTwiceIsRefused()
    {
        var problem = Problem("settings:\n  forwardSkipTime:\n    value: 30\nother:\n  homePage: Home\nsettings:\n  rewindSkipTime:\n    value: 5\n");

        Assert.NotNull(problem);
        Assert.Contains("duplicate key settings", problem, StringComparison.Ordinal);
        Assert.StartsWith("Line 6, column 1: ", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every refusal the reader raises says where, which YamlDotNet keeps beside its message
    /// rather than in it: a broken document read "No node deserializer was able to deserialize
    /// the node into type" and a .NET type name, and no line.
    /// </summary>
    [Fact]
    public void ABrokenDocumentSaysWhere()
    {
        Assert.StartsWith("Line 2, column ", Problem("settings:\n  forwardSkipTime: [\n") ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same inside a block: a setting twice, and a value twice in one setting.
    /// </summary>
    /// <param name="yaml">The document.</param>
    /// <param name="key">The key written twice.</param>
    [Theory]
    [InlineData("settings:\n  forwardSkipTime:\n    value: 30\n  forwardSkipTime:\n    value: 10\n", "forwardSkipTime")]
    [InlineData("settings:\n  forwardSkipTime:\n    value: 30\n    value: 10\n", "value")]
    [InlineData("notifications:\n  itemAdded:\n    enabled: true\n  itemAdded:\n    enabled: false\n", "itemAdded")]
    public void AKeyWrittenTwiceInsideIsRefused(string yaml, string key)
    {
        Assert.Contains($"duplicate key {key}", Problem(yaml) ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// One key in two mappings is two keys, and is stored.
    /// </summary>
    [Fact]
    public void TheSameKeyInTwoMappingsIsTwoKeys()
    {
        Assert.Null(Problem("settings:\n  forwardSkipTime:\n    value: 30\n  rewindSkipTime:\n    value: 30\n"));
    }

    /// <summary>
    /// The examples this repository ships still read: none holds a key twice.
    /// </summary>
    [Fact]
    public void TheExamplesHoldNoKeyTwice()
    {
        var examples = Directory.EnumerateFiles(Path.Combine(SourceTree.Root(), "examples")).ToList();

        Assert.NotEmpty(examples);
        foreach (var example in examples)
        {
            var read = Record.Exception(() => _serialization.Deserialize<Config>(File.ReadAllText(example)));
            Assert.True(read is null, $"{Path.GetFileName(example)}: {read?.Message}");
        }
    }
}
