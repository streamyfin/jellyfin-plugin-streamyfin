using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// An administrator who goes back to 0.68.1.0 and then updates again. The old version
/// only knows Jellyfin's plugin XML, so whatever it changed lives there, and this version
/// read that file once, at the import.
/// </summary>
/// <remarks>
/// Found on two throwaway servers, 10.11.11 and 12.0.0: a setting changed while the old
/// version ran was served with its earlier value after the update, and nothing in the log
/// said so. Nothing is taken over from the file, since an older version writes it without
/// the settings it does not know and Jellyfin fills it with that version's defaults when
/// it is missing, but every change is named once. Each test here is one start of the
/// plugin reading the file as it stands.
/// </remarks>
public class DowngradeRoundTripTests : IDisposable
{
    private readonly string _directory = TestDirectory.Create();
    private readonly SerializationHelper _serialization = new();
    private readonly RecordingLogger<GlobalConfigurationStore> _logger = new();

    private static Config File(int forward, int? rewind = 20, string? homePage = null) => new()
    {
        settings = new Settings
        {
            forwardSkipTime = new Lockable<int> { locked = true, value = forward },
            rewindSkipTime = rewind is null ? null : new Lockable<int> { value = rewind.Value }
        },
        Other = homePage is null ? null : new Other { HomePage = homePage }
    };

    private static Config WithSeerrKey(string key) => new()
    {
        settings = new Settings
        {
            jellyseerrApiKey = new Lockable<string> { value = key }
        }
    };

    /// <summary>
    /// What Jellyfin hands the plugin after reading its own file back.
    /// </summary>
    /// <remarks>
    /// Not the same object that was written: the XML serializer creates every list it
    /// meets, so a collection left null comes back empty.
    /// </remarks>
    private static Config ThroughXml(Config config)
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new PluginConfiguration { Config = config });
        using var reader = new StringReader(writer.ToString());
        return ((PluginConfiguration)serializer.Deserialize(reader)!).Config;
    }

    /// <summary>
    /// One start of the plugin: the database is opened, then the file is read.
    /// </summary>
    private GlobalConfigurationStore Start(Config? file)
    {
        var store = new GlobalConfigurationStore(new PluginDatabase(_directory), _serialization, _logger);
        store.Import(file, null);
        return store;
    }

    private bool Mentions(string key) =>
        _logger.Messages.Any(message => message.Contains(key, StringComparison.Ordinal));

    private string Row(string id)
    {
        using var context = new PluginDatabase(_directory).CreateContext();
        return context.GlobalConfigurations.Single(c => c.Id == id).ConfigJson;
    }

    private void Row(string id, string json)
    {
        using var context = new PluginDatabase(_directory).CreateContext();
        context.GlobalConfigurations.Single(c => c.Id == id).ConfigJson = json;
        context.SaveChanges();
    }

    /// <summary>
    /// A setting the old version changed is named, and the value here stays the one in use.
    /// </summary>
    [Fact]
    public void AChangeTheOldVersionMadeIsNamedAndNotApplied()
    {
        Start(File(forward: 45));

        var store = Start(File(forward: 60));

        Assert.Equal(45, store.Current.settings?.forwardSkipTime?.value);
        Assert.True(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// The configuration is not written at all, which a fresh store reading it proves
    /// rather than the cache of the one that compared.
    /// </summary>
    [Fact]
    public void NothingIsWrittenToTheConfiguration()
    {
        Start(File(forward: 45, homePage: "Application"));
        Start(File(forward: 60, rewind: 33, homePage: "Targeting"));

        var fresh = new GlobalConfigurationStore(new PluginDatabase(_directory), _serialization);

        Assert.Equal(45, fresh.Current.settings?.forwardSkipTime?.value);
        Assert.Equal(20, fresh.Current.settings?.rewindSkipTime?.value);
        Assert.Equal("Application", fresh.Current.Other?.HomePage);
    }

    /// <summary>
    /// A change made here, on a file that did not move, needs nothing said.
    /// </summary>
    [Fact]
    public void AChangeMadeHereIsNotReported()
    {
        Start(File(forward: 45)).Save(File(forward: 50));

        var store = Start(File(forward: 45));

        Assert.Equal(50, store.Current.settings?.forwardSkipTime?.value);
        Assert.False(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// Changed on both sides, the value here stays and the setting is named.
    /// </summary>
    [Fact]
    public void ASettingChangedOnBothSidesIsNamed()
    {
        Start(File(forward: 45)).Save(File(forward: 51));

        var store = Start(File(forward: 57));

        Assert.Equal(51, store.Current.settings?.forwardSkipTime?.value);
        Assert.True(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// Only what the file changed is named, not what was changed here.
    /// </summary>
    [Fact]
    public void OnlyTheSettingsTheFileChangedAreNamed()
    {
        Start(File(forward: 45, rewind: 20)).Save(File(forward: 45, rewind: 25));

        Start(File(forward: 60, rewind: 20));

        Assert.True(Mentions("settings.forwardSkipTime"));
        Assert.False(Mentions("settings.rewindSkipTime"));
    }

    /// <summary>
    /// The same change made on both sides leaves nothing to set again.
    /// </summary>
    [Fact]
    public void AChangeAlreadyMadeHereIsNotReported()
    {
        Start(File(forward: 45)).Save(File(forward: 60));

        Start(File(forward: 60));

        Assert.False(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// An entry the file gained is a change like any other.
    /// </summary>
    [Fact]
    public void AnEntryTheFileGainedIsNamed()
    {
        Start(File(forward: 45));

        Start(File(forward: 45, homePage: "Targeting"));

        Assert.True(Mentions("other.homePage"));
    }

    /// <summary>
    /// An entry missing from the file says nothing. An older version drops what it does
    /// not know when it writes the file, so naming every absence would name every setting
    /// it never had.
    /// </summary>
    [Fact]
    public void AnEntryMissingFromTheFileIsNotReported()
    {
        Start(File(forward: 45, rewind: 20));

        var store = Start(File(forward: 45, rewind: null));

        Assert.Equal(20, store.Current.settings?.rewindSkipTime?.value);
        Assert.False(Mentions("settings.rewindSkipTime"));
    }

    /// <summary>
    /// A change is named once. The file still carries it on later starts, and saying so
    /// every time would train an administrator to skip the line.
    /// </summary>
    [Fact]
    public void AChangeIsNamedOnce()
    {
        Start(File(forward: 45));
        Start(File(forward: 60));
        _logger.Messages.Clear();

        Start(File(forward: 60));

        Assert.False(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// A start that fails to compare leaves the copy as it was, so the next one still
    /// names the change, which is what the failure's log line promises.
    /// </summary>
    [Fact]
    public void AChangeIsStillNamedAfterAStartThatCouldNotCompare()
    {
        Start(File(forward: 45));
        var stored = Row(GlobalConfiguration.Current);

        Row(GlobalConfiguration.Current, "{ this is not json");
        Start(File(forward: 60));
        Assert.True(Mentions("compared again on the next start"));
        Assert.False(Mentions("settings.forwardSkipTime"));

        Row(GlobalConfiguration.Current, stored);
        Start(File(forward: 60));
        Assert.True(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// A server imported by a build that kept no copy of the file cannot tell what changed
    /// before this build ran, so it says nothing about that and watches from there.
    /// </summary>
    [Fact]
    public void AServerImportedByAnEarlierBuildIsWatchedFromNow()
    {
        Start(File(forward: 45, rewind: 20));
        using (var context = new PluginDatabase(_directory).CreateContext())
        {
            context.GlobalConfigurations.RemoveRange(
                context.GlobalConfigurations.Where(c => c.Id == GlobalConfiguration.LegacyFile));
            context.SaveChanges();
        }

        Start(File(forward: 60, rewind: 20));
        Assert.False(Mentions("settings.forwardSkipTime"));

        Start(File(forward: 60, rewind: 30));
        Assert.True(Mentions("settings.rewindSkipTime"));
        Assert.False(Mentions("settings.forwardSkipTime"));
    }

    /// <summary>
    /// Jellyfin writes this version's defaults over a file that is missing or unreadable.
    /// Nobody chose those values, so they are not listed as changes.
    /// </summary>
    [Fact]
    public void AFileJellyfinRewroteWithTheDefaultsIsNotAChange()
    {
        Start(File(forward: 45));

        var store = Start(PluginConfiguration.DefaultConfig());

        Assert.Equal(45, store.Current.settings?.forwardSkipTime?.value);
        Assert.False(Mentions("settings."));
    }

    /// <summary>
    /// A fresh server imports the defaults Jellyfin just wrote, as objects, and reads the
    /// same file back on its next start. Nothing changed, and nothing is said.
    /// </summary>
    /// <remarks>
    /// Seen on a real 12.0.0 server: the home sections came back with an empty genre list
    /// the defaults never had, and were taken for a change.
    /// </remarks>
    [Fact]
    public void AFileReadBackByJellyfinIsNotAChange()
    {
        Start(PluginConfiguration.DefaultConfig());
        _logger.Messages.Clear();

        Start(ThroughXml(PluginConfiguration.DefaultConfig()));

        Assert.Empty(_logger.Messages);
    }

    /// <summary>
    /// The same after Jellyfin replaced a missing file with its defaults: the start after
    /// the one that noticed says nothing more.
    /// </summary>
    [Fact]
    public void ADefaultsFileReadBackSaysNothingMore()
    {
        Start(File(forward: 45));
        Start(PluginConfiguration.DefaultConfig());
        _logger.Messages.Clear();

        Start(ThroughXml(PluginConfiguration.DefaultConfig()));

        Assert.Empty(_logger.Messages);
    }

    /// <summary>
    /// A secret is named, and neither of its values reaches the log.
    /// </summary>
    [Fact]
    public void ASecretIsNamedButNeverWritten()
    {
        Start(WithSeerrKey("key-at-import")).Save(WithSeerrKey("key-set-here"));

        var store = Start(WithSeerrKey("key-set-there"));

        Assert.Equal("key-set-here", store.Current.settings?.jellyseerrApiKey?.value);
        Assert.True(Mentions("settings.jellyseerrApiKey"));
        Assert.DoesNotContain(_logger.Messages, m => m.Contains("key-set-", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Messages, m => m.Contains("key-at-", StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
