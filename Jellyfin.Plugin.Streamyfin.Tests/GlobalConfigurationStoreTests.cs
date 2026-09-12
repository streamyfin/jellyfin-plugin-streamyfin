using System;
using System.Xml.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The configuration the server declares for everyone, which P1.5 moved out of
/// Jellyfin's plugin XML and into the plugin's own database, so that all three
/// targeting levels live in one store.
/// </summary>
public class GlobalConfigurationStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;
    private readonly SerializationHelper _serialization = new();
    private readonly GlobalConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalConfigurationStoreTests"/> class.
    /// </summary>
    public GlobalConfigurationStoreTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
        _store = new GlobalConfigurationStore(_db, _serialization);
    }

    private static Config Sample(int subtitleSize) => new()
    {
        settings = new Settings
        {
            subtitleSize = new Lockable<int> { locked = true, value = subtitleSize }
        }
    };

    /// <summary>
    /// A server that has never been configured reads as an empty configuration rather
    /// than as null, so every caller sees "the server has no opinion".
    /// </summary>
    [Fact]
    public void AnEmptyStoreReadsAsAnEmptyConfiguration()
    {
        Assert.NotNull(_store.Current);
        Assert.Null(_store.Current.settings);
    }

    /// <summary>
    /// What is written comes back, through a second store so the answer cannot come from
    /// the first one's cache.
    /// </summary>
    [Fact]
    public void WhatIsWrittenComesBack()
    {
        _store.Save(Sample(120));

        var fresh = new GlobalConfigurationStore(_db, _serialization);

        Assert.Equal(120, fresh.Current.settings?.subtitleSize?.value);
        Assert.True(fresh.Current.settings?.subtitleSize?.locked);
    }

    /// <summary>
    /// A write is visible immediately on the store that made it. The value is held in
    /// memory between writes, and a stale read after a save would be worse than no cache.
    /// </summary>
    [Fact]
    public void AWriteIsVisibleImmediately()
    {
        _store.Save(Sample(80));
        Assert.Equal(80, _store.Current.settings?.subtitleSize?.value);

        _store.Save(Sample(60));
        Assert.Equal(60, _store.Current.settings?.subtitleSize?.value);
    }

    /// <summary>
    /// The import carries the old configuration over.
    /// </summary>
    [Fact]
    public void TheImportCarriesTheOldConfigurationOver()
    {
        _store.Import(Sample(42), null);

        Assert.Equal(42, _store.Current.settings?.subtitleSize?.value);
    }

    /// <summary>
    /// It runs once. Running again would undo every change made since, which on a server
    /// that has been running for months is the whole configuration.
    /// </summary>
    [Fact]
    public void TheImportRunsOnce()
    {
        _store.Import(Sample(42), null);
        _store.Save(Sample(99));

        var restarted = new GlobalConfigurationStore(_db, _serialization);
        restarted.Import(Sample(42), null);

        Assert.Equal(99, restarted.Current.settings?.subtitleSize?.value);
    }

    /// <summary>
    /// A server with nothing in its old file imports an empty configuration rather than
    /// skipping the import, so the marker is written and it is not retried every start.
    /// </summary>
    [Fact]
    public void ImportingNothingStillMarksItDone()
    {
        _store.Import(null, null);
        _store.Save(Sample(77));

        var restarted = new GlobalConfigurationStore(_db, _serialization);
        restarted.Import(null, null);

        Assert.Equal(77, restarted.Current.settings?.subtitleSize?.value);
    }

    /// <summary>
    /// Settings the old file carries that this version no longer declares are found, so
    /// they can be reported. The XML deserializer drops them silently and before anything
    /// here sees them, so an administrator has been running with values that do nothing.
    /// </summary>
    /// <remarks>
    /// The three names are not invented. They are what a real server's configuration file
    /// still carried while this was written.
    /// </remarks>
    [Fact]
    public void SettingsThisVersionNoLongerDeclaresAreFound()
    {
        var document = XDocument.Parse(
            """
            <PluginConfiguration>
              <Config>
                <settings>
                  <subtitleSize><locked>false</locked><value>60</value></subtitleSize>
                  <autoDownload><locked>false</locked><value>true</value></autoDownload>
                  <downloadMethod><locked>false</locked><value>remux</value></downloadMethod>
                  <remuxConcurrentLimit><locked>false</locked><value>2</value></remuxConcurrentLimit>
                </settings>
              </Config>
            </PluginConfiguration>
            """);

        Assert.Equal(
            new[] { "autoDownload", "downloadMethod", "remuxConcurrentLimit" },
            GlobalConfigurationStore.UnknownSettingKeys(document));
    }

    /// <summary>
    /// A file whose settings this version all declares reports nothing.
    /// </summary>
    [Fact]
    public void AFileThisVersionUnderstandsReportsNothing()
    {
        var document = XDocument.Parse(
            """
            <PluginConfiguration>
              <Config>
                <settings>
                  <subtitleSize><locked>false</locked><value>60</value></subtitleSize>
                  <forwardSkipTime><locked>false</locked><value>15</value></forwardSkipTime>
                </settings>
              </Config>
            </PluginConfiguration>
            """);

        Assert.Empty(GlobalConfigurationStore.UnknownSettingKeys(document));
    }

    /// <summary>
    /// A file with no settings block at all is not a failure.
    /// </summary>
    [Fact]
    public void AFileWithNoSettingsIsNotAFailure()
    {
        Assert.Empty(GlobalConfigurationStore.UnknownSettingKeys(
            XDocument.Parse("<PluginConfiguration><Config /></PluginConfiguration>")));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
