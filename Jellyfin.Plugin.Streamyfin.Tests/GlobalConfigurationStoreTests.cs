using System;
using System.Linq;
using Microsoft.Data.Sqlite;
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
    /// A configuration stored with Seerr's block beside the keys, as the builds of develop
    /// since #198 wrote it, is read with the keys alone. Served back with the block, the
    /// next save that changed a key by itself disagreed with it and was refused.
    /// </summary>
    [Fact]
    public void ABlockStoredBesideTheKeysIsReadAsTheKeys()
    {
        var address = new Lockable<string> { value = "http://seerr.example" };
        _db.SaveGlobalConfigJson(_serialization.SerializeToJson(new Config
        {
            settings = new Settings
            {
                seerr = new SeerrSettings { serverUrl = address },
                jellyseerrServerUrl = new Lockable<string> { value = address.value }
            }
        }));

        var settings = new GlobalConfigurationStore(_db, _serialization).Current.settings;

        Assert.Null(settings?.seerr);
        Assert.Equal("http://seerr.example", settings?.jellyseerrServerUrl?.value);
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
    /// A configuration a preview stored still loads once a setting it holds is no longer
    /// declared, and the rest of it is still served.
    /// </summary>
    /// <remarks>
    /// videoPlayer, deviceProfile and three settings the app reads nowhere left the plugin
    /// between two unstable builds. A server that saved one of them keeps it in its
    /// database, and refusing the whole document over it would serve nothing at all.
    /// </remarks>
    [Fact]
    public void AStoredSettingThisVersionNoLongerDeclaresIsLeftBehind()
    {
        _db.SaveGlobalConfigJson(
            """
            {"settings": {
              "videoPlayer": {"locked": true, "value": 1},
              "deviceProfile": {"locked": false, "value": "Expo"},
              "showHomeTitles": {"locked": false, "value": true},
              "usePopularPlugin": {"locked": false, "value": true},
              "mediaListCollectionIds": {"locked": false, "value": []},
              "forwardSkipTime": {"locked": true, "value": 15}
            }}
            """);

        var current = new GlobalConfigurationStore(_db, _serialization).Current;

        Assert.Equal(15, current.settings?.forwardSkipTime?.value);
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
    /// <summary>
    /// A server that never said anything about the awaited titles has them turned on, once,
    /// and the pages then show them on (#225).
    /// </summary>
    [Fact]
    public void TheAwaitedTitlesAreSwitchedOnOnAServerThatNeverMentionedThem()
    {
        _store.Save(new Config { notifications = new Configuration.Notifications.Notifications() });

        _store.SwitchOnAwaitedTitles();

        Assert.True(new GlobalConfigurationStore(_db, _serialization).Current.notifications?.AwaitedTitle?.Enabled);
    }

    /// <summary>
    /// Turned off by an administrator afterwards, they stay off across restarts.
    /// </summary>
    [Fact]
    public void AnAdministratorWhoTurnsThemOffAfterwardsKeepsThemOff()
    {
        _store.Save(new Config { notifications = new Configuration.Notifications.Notifications() });
        _store.SwitchOnAwaitedTitles();

        var config = _store.Current;
        config.notifications!.AwaitedTitle = new Configuration.Notifications.NotificationConfiguration { Enabled = false };
        _store.Save(config);

        new GlobalConfigurationStore(_db, _serialization).SwitchOnAwaitedTitles();

        Assert.False(new GlobalConfigurationStore(_db, _serialization).Current.notifications?.AwaitedTitle?.Enabled);
    }

    /// <summary>
    /// A server that already says something about them is left as it is.
    /// </summary>
    [Fact]
    public void AServerThatAlreadySaysSomethingAboutThemIsLeftAsItIs()
    {
        _store.Save(new Config
        {
            notifications = new Configuration.Notifications.Notifications
            {
                AwaitedTitle = new Configuration.Notifications.NotificationConfiguration { Enabled = false }
            }
        });

        _store.SwitchOnAwaitedTitles();

        Assert.False(_store.Current.notifications?.AwaitedTitle?.Enabled);
    }

    // What a server that installed 0.68.1.0 and never set these stored by default, beside one
    // setting an administrator chose.
    private static Config FromADefault068() => new()
    {
        settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "Enter jellyseerr server url" },
            marlinServerUrl = new Lockable<string> { value = "Enter Marlin server URL" },
            hiddenLibraries = new Lockable<string[]> { value = ["Enter library id(s)"] },
            forwardSkipTime = new Lockable<int> { value = 45, locked = true }
        }
    };

    /// <summary>
    /// The placeholders a default configuration of 0.66 to 0.68.1.0 carried are taken out of
    /// the stored configuration, and the log names the settings the app decides again (#279).
    /// What an administrator set beside them stays.
    /// </summary>
    [Fact]
    public void ThePlaceholdersAnEarlierVersionStoredAreTakenOut()
    {
        var logger = new RecordingLogger<GlobalConfigurationStore>();
        var store = new GlobalConfigurationStore(_db, _serialization, logger);
        store.Import(FromADefault068(), null);

        store.ForgetPlaceholders();

        var stored = _serialization.DeserializeJson<Config>(_db.GetGlobalConfigJson()!)!.settings!;
        Assert.Null(stored.jellyseerrServerUrl);
        Assert.Null(stored.marlinServerUrl);
        Assert.Null(stored.hiddenLibraries);
        Assert.Equal(45, stored.forwardSkipTime!.value);
        Assert.True(stored.forwardSkipTime.locked);

        var said = Assert.Single(logger.Messages, message => message.Contains("placeholder", StringComparison.Ordinal));
        Assert.Equal("Took out the placeholder text an earlier version of the plugin stored in hiddenLibraries, jellyseerrServerUrl, marlinServerUrl", said);
    }

    /// <summary>
    /// The plugin's start names what it takes out on a server moved from 0.68.1.0, whose stored
    /// configuration never mentioned the awaited titles. Switching them on saved the
    /// configuration in use, which holds no example text, before the clean up looked, and the
    /// clean up then found none to name.
    /// </summary>
    [Fact]
    public void TheStartNamesTheExampleTextItTakesOut()
    {
        var logger = new RecordingLogger<GlobalConfigurationStore>();
        var store = new GlobalConfigurationStore(_db, _serialization, logger);

        store.Open(FromADefault068(), null);

        Assert.Contains(logger.Messages, message => message.StartsWith("Took out the placeholder text", StringComparison.Ordinal));
        Assert.True(new GlobalConfigurationStore(_db, _serialization).Current.notifications?.AwaitedTitle?.Enabled);
    }

    /// <summary>
    /// The configuration in use carries none even before they are taken out of the database, so
    /// no route serves them, the ones that read it without tidying included.
    /// </summary>
    [Fact]
    public void TheConfigurationInUseCarriesNoPlaceholderBeforeTheyAreTakenOut()
    {
        _store.Import(FromADefault068(), null);

        var settings = new GlobalConfigurationStore(_db, _serialization).Current.settings!;

        Assert.Null(settings.jellyseerrServerUrl);
        Assert.Null(settings.marlinServerUrl);
        Assert.Null(settings.hiddenLibraries);
        Assert.Equal(45, settings.forwardSkipTime!.value);
    }

    /// <summary>
    /// A database that cannot be written at start-up costs the clean up and the switch, which the
    /// next start tries again, and not the plugin: an exception out of the plugin's constructor
    /// leaves it Malfunctioned until an administrator turns it on again.
    /// </summary>
    [Fact]
    public void ADatabaseThatCannotBeWrittenDoesNotStopTheStart()
    {
        _store.Import(FromADefault068(), null);
        var logger = new RecordingLogger<GlobalConfigurationStore>();
        var store = new GlobalConfigurationStore(_db, _serialization, logger);
        SqliteConnection.ClearAllPools();
        System.IO.File.WriteAllText(_db.DbFilePath, "not a database");

        var thrown = Record.Exception(() =>
        {
            store.ForgetPlaceholders();
            store.SwitchOnAwaitedTitles();
        });

        Assert.Null(thrown);
        Assert.Equal(2, logger.Messages.Count(message => message.Contains("tried again on the next start", StringComparison.Ordinal)));
    }

    /// <summary>
    /// They are taken out for good: the next start finds none and says nothing.
    /// </summary>
    [Fact]
    public void ThePlaceholdersAreTakenOutForGood()
    {
        _store.Import(FromADefault068(), null);
        _store.ForgetPlaceholders();

        var logger = new RecordingLogger<GlobalConfigurationStore>();
        var later = new GlobalConfigurationStore(_db, _serialization, logger);
        later.ForgetPlaceholders();

        Assert.DoesNotContain(logger.Messages, message => message.Contains("placeholder", StringComparison.Ordinal));
    }

    /// <summary>
    /// A server without any says nothing, and its configuration is not written.
    /// </summary>
    [Fact]
    public void AServerWithoutPlaceholdersSaysNothing()
    {
        _store.Save(new Config { settings = new Settings { forwardSkipTime = new Lockable<int> { value = 30 } } });
        var before = _db.GetGlobalConfigJson();
        var logger = new RecordingLogger<GlobalConfigurationStore>();

        new GlobalConfigurationStore(_db, _serialization, logger).ForgetPlaceholders();

        Assert.Equal(before, _db.GetGlobalConfigJson());
        Assert.DoesNotContain(logger.Messages, message => message.Contains("placeholder", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
