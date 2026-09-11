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
