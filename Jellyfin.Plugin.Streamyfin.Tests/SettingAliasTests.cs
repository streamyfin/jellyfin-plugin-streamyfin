using Jellyfin.Plugin.Streamyfin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A setting answering to more than one spelling.
/// </summary>
/// <remarks>
/// Jellyseerr was renamed Seerr. Issue #95 is an administrator writing
/// <c>seerrServerUrl</c>, the plugin ignoring it in silence, and the app never seeing a
/// server.
/// </remarks>
public class SettingAliasTests
{
    private readonly SerializationHelper _serialization = new();

    /// <summary>
    /// The current spelling is read.
    /// </summary>
    [Fact]
    public void TheCurrentSpellingIsRead()
    {
        var config = _serialization.Deserialize<Config>("""
            settings:
              seerrServerUrl:
                value: https://requests.example.com
              seerrApiKey:
                value: a-key
              autoLoginSeerr:
                value: false
            """);

        Assert.Equal("https://requests.example.com", config.settings?.jellyseerrServerUrl?.value);
        Assert.Equal("a-key", config.settings?.jellyseerrApiKey?.value);
        Assert.False(config.settings?.autoLoginJellyseerr?.value);
    }

    /// <summary>
    /// The old spelling is still read, because it is what every stored configuration and
    /// every copy of the app in the field uses.
    /// </summary>
    [Fact]
    public void TheOldSpellingIsStillRead()
    {
        var config = _serialization.Deserialize<Config>("""
            settings:
              jellyseerrServerUrl:
                value: https://old.example.com
                locked: true
            """);

        Assert.Equal("https://old.example.com", config.settings?.jellyseerrServerUrl?.value);
        Assert.True(config.settings?.jellyseerrServerUrl?.locked);
    }

    /// <summary>
    /// Only one spelling comes back out, so a document does not grow a second copy of a
    /// setting every time it is saved.
    /// </summary>
    [Fact]
    public void OnlyOneSpellingIsWritten()
    {
        var yaml = _serialization.SerializeToYaml(_serialization.Deserialize<Config>("""
            settings:
              seerrServerUrl:
                value: https://requests.example.com
            """));

        Assert.Contains("jellyseerrServerUrl", yaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("seerrServerUrl:", yaml.Replace("jellyseerrServerUrl:", string.Empty, System.StringComparison.Ordinal), System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A setting with no alias is unaffected, which is every other setting.
    /// </summary>
    [Fact]
    public void ASettingWithNoAliasIsUnaffected()
    {
        var config = _serialization.Deserialize<Config>("""
            settings:
              marlinServerUrl:
                value: https://marlin.example.com
            """);

        Assert.Equal("https://marlin.example.com", config.settings?.marlinServerUrl?.value);
    }

    /// <summary>
    /// One setting written under two of its names is refused, whichever order they come
    /// in.
    /// </summary>
    /// <remarks>
    /// YamlDotNet's own answer is that the later key wins, silently, and the whole
    /// stored pair goes with it, <c>locked</c> included. Silence is what made #95 worth
    /// reporting, so the collision says so and names both spellings.
    /// </remarks>
    /// <param name="first">The name written first.</param>
    /// <param name="second">The name written second.</param>
    [Theory]
    [InlineData("jellyseerrServerUrl", "seerrServerUrl")]
    [InlineData("seerrServerUrl", "jellyseerrServerUrl")]
    public void OneSettingUnderTwoNamesIsRefused(string first, string second)
    {
        var thrown = Assert.ThrowsAny<System.Exception>(() => _serialization.Deserialize<Config>($"""
            settings:
              {first}:
                value: https://one.example.com
                locked: true
              {second}:
                value: https://two.example.com
            """));

        var said = Said(thrown);

        Assert.Contains("jellyseerrServerUrl", said, System.StringComparison.Ordinal);
        Assert.Contains(second, said, System.StringComparison.Ordinal);
        Assert.Contains("written under one name", said, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The same setting written twice under the same name is YamlDotNet's own business,
    /// and this does not change it.
    /// </summary>
    [Fact]
    public void TwoSettingsThatAreNotTheSameAreFine()
    {
        var config = _serialization.Deserialize<Config>("""
            settings:
              seerrServerUrl:
                value: https://requests.example.com
              seerrApiKey:
                value: a-key
              marlinServerUrl:
                value: https://marlin.example.com
            """);

        Assert.Equal("https://requests.example.com", config.settings?.jellyseerrServerUrl?.value);
        Assert.Equal("a-key", config.settings?.jellyseerrApiKey?.value);
        Assert.Equal("https://marlin.example.com", config.settings?.marlinServerUrl?.value);
    }

    private static string Said(System.Exception thrown)
    {
        var said = thrown.Message;

        for (var inner = thrown.InnerException; inner is not null; inner = inner.InnerException)
        {
            said += " " + inner.Message;
        }

        return said;
    }

    /// <summary>
    /// A key that is neither a name nor an alias is still refused, and says which key it
    /// was.
    /// </summary>
    /// <remarks>
    /// The refusal is what the Yaml tab shows an administrator who mistypes a setting,
    /// and it is the reason #95 was worth fixing here rather than by accepting anything:
    /// silence is the failure, not strictness.
    /// </remarks>
    [Fact]
    public void AKeyThatIsNeitherIsStillRefused()
    {
        var thrown = Assert.Throws<YamlDotNet.Core.YamlException>(() => _serialization.Deserialize<Config>("""
            settings:
              seerrServerUrlz:
                value: https://requests.example.com
            """));

        Assert.Contains("seerrServerUrlz", thrown.Message, System.StringComparison.Ordinal);
    }
}
