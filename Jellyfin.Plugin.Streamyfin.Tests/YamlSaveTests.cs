using System;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Saving a Yaml document the way <c>POST config/yaml</c> does, down to the store the plugin
/// keeps its configuration in.
/// </summary>
/// <remarks>
/// <see cref="EmptyBodiesTests"/> holds what is decided about a document. This holds what the
/// route does with the decision: a refused document leaves what is stored as it was, which a
/// save that ran after a refusal would break while every decision still read right.
/// </remarks>
public class YamlSaveTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;
    private readonly SerializationHelper _serialization = new();
    private readonly GlobalConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlSaveTests"/> class, with a stored
    /// configuration that holds a setting and an event.
    /// </summary>
    public YamlSaveTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
        _store = new GlobalConfigurationStore(_db, _serialization);
        _store.Save(new Config
        {
            settings = new Settings { forwardSkipTime = new Lockable<int> { value = 30 } },
            notifications = new Notifications { ItemAdded = new ItemAddedNotificationConfiguration { Enabled = true } }
        });
    }

    // Read from the database rather than from the store's memory, which is what a restart reads.
    private string Stored() => _serialization.SerializeToJson(new GlobalConfigurationStore(_db, _serialization).Current);

    /// <summary>
    /// A document that is refused stores nothing.
    /// </summary>
    /// <param name="yaml">What the editor sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("settings: {}\n")]
    [InlineData("settings:\n  defaultPlaybackSpeed:\n    value: .nan\n")]
    [InlineData("settings: [unclosed\n")]
    public void ARefusedDocumentLeavesWhatIsStoredAsItWas(string yaml)
    {
        var before = Stored();

        var answer = StreamyfinController.SaveYaml(_serialization, yaml, _store);

        Assert.True(answer.Error);
        Assert.False(string.IsNullOrWhiteSpace(answer.Message));
        Assert.Equal(before, Stored());
    }

    /// <summary>
    /// A document that sets something becomes the configuration, as the editor means it to.
    /// </summary>
    [Fact]
    public void ADocumentThatSetsSomethingIsStored()
    {
        var answer = StreamyfinController.SaveYaml(_serialization, "settings:\n  forwardSkipTime:\n    value: 10\n", _store);

        Assert.False(answer.Error);
        var stored = new GlobalConfigurationStore(_db, _serialization).Current;
        Assert.Equal(10, stored.settings?.forwardSkipTime?.value);
        Assert.Null(stored.notifications?.ItemAdded);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
