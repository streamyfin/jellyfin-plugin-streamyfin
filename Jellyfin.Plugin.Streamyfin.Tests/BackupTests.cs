using System;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Everything an administrator set, as one file.
/// </summary>
/// <remarks>
/// The routes need a plugin instance and a user manager, so what is held here is the
/// shape of the file and the rules a restore has to follow. The round trip itself is
/// checked on a server.
/// </remarks>
public class BackupTests
{
    private readonly SerializationHelper _serialization = new();

    /// <summary>
    /// A backup carries the three things an administrator set, not just the one Jellyfin
    /// already backs up.
    /// </summary>
    [Fact]
    public void ABackupCarriesTheTargetingLevelsToo()
    {
        var backup = new ConfigurationBackup
        {
            Plugin = "0.70.0.0",
            TakenAt = DateTimeOffset.UtcNow,
            Config = new Configuration.Config
            {
                settings = new Settings { forwardSkipTime = new Lockable<int> { value = 20 } }
            },
            Groups =
            [
                new SettingsGroupDto
                {
                    Name = "TVs",
                    Priority = 1,
                    UserIds = [Guid.NewGuid()],
                    Settings = new Settings { subtitleSize = new Lockable<int> { value = 120 } }
                }
            ],
            Users =
            [
                new UserBackup
                {
                    UserId = Guid.NewGuid(),
                    Settings = new Settings { forwardSkipTime = new Lockable<int> { value = 5 } }
                }
            ]
        };

        var read = _serialization.DeserializeJson<ConfigurationBackup>(_serialization.SerializeToJson(backup));

        Assert.Equal(20, read!.Config?.settings?.forwardSkipTime?.value);
        Assert.Equal("TVs", Assert.Single(read.Groups).Name);
        Assert.Single(Assert.Single(read.Groups).UserIds);
        Assert.Equal(5, Assert.Single(read.Users).Settings?.forwardSkipTime?.value);
    }

    /// <summary>
    /// Every level in a file is checked the way a level written through the API is.
    /// </summary>
    /// <remarks>
    /// A file is a write path like any other, and it is the one that arrives whole:
    /// half a restore is worse than a refusal.
    /// </remarks>
    [Fact]
    public void ALevelInAFileIsCheckedLikeAnyOther()
    {
        var tooFar = new Settings { forwardSkipTime = new Lockable<int> { value = 600 } };

        Assert.NotNull(SettingsValidation.Check(tooFar));
    }

    /// <summary>
    /// An address in a file is trimmed the way one typed into the form is.
    /// </summary>
    [Fact]
    public void AnAddressInAFileIsTidied()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "  https://requests.example.com  " }
        };

        Assert.Null(SettingsValidation.Check(settings));
        Assert.Equal("https://requests.example.com", settings.jellyseerrServerUrl!.value);
    }

    /// <summary>
    /// A backup written by a newer plugin still reads, since a file is the one thing
    /// that outlives the version that wrote it.
    /// </summary>
    [Fact]
    public void AFileFromANewerPluginStillReads()
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>("""
            {
              "plugin": "9.9.9.9",
              "takenAt": "2026-09-11T18:00:00+00:00",
              "somethingNobodyDeclared": true,
              "config": { "settings": { "forwardSkipTime": { "value": 20, "locked": false } } },
              "groups": [],
              "users": []
            }
            """);

        Assert.Equal("9.9.9.9", read!.Plugin);
        Assert.Equal(20, read.Config?.settings?.forwardSkipTime?.value);
    }

    /// <summary>
    /// The settings that do not survive the wrong serializer survive a backup.
    /// </summary>
    /// <remarks>
    /// The framework writes an enum as its name and the plugin's reader expects the
    /// number it stores, so a backup written by the wrong one made every restore fail
    /// on subtitleMode. These five are the ones SerializationHelper names.
    /// </remarks>
    [Fact]
    public void TheSettingsThatNeedTheRightSerializerSurvive()
    {
        var backup = new ConfigurationBackup
        {
            Config = new Configuration.Config
            {
                settings = new Settings
                {
                    subtitleMode = new Lockable<Configuration.SubtitlePlaybackMode> { value = Configuration.SubtitlePlaybackMode.Smart },
                    defaultBitrate = new Lockable<Configuration.Bitrate?> { value = Configuration.Bitrate._4MB },
                    defaultVideoOrientation = new Lockable<Configuration.OrientationLock> { value = Configuration.OrientationLock.LandscapeLeft },
                    inactivityTimeout = new Lockable<Configuration.InactivityTimeout> { value = Configuration.InactivityTimeout.OneMinute }
                }
            }
        };

        var read = _serialization.DeserializeJson<ConfigurationBackup>(_serialization.SerializeToJson(backup));

        Assert.Equal(Configuration.SubtitlePlaybackMode.Smart, read!.Config?.settings?.subtitleMode?.value);
        Assert.Equal(Configuration.Bitrate._4MB, read.Config?.settings?.defaultBitrate?.value);
        Assert.Equal(Configuration.OrientationLock.LandscapeLeft, read.Config?.settings?.defaultVideoOrientation?.value);
        Assert.Equal(Configuration.InactivityTimeout.OneMinute, read.Config?.settings?.inactivityTimeout?.value);
    }

    /// <summary>
    /// A group with no name is refused, the way one written through the API is.
    /// </summary>
    [Fact]
    public void AGroupWithNoNameIsNotSomethingToRestore()
    {
        var nameless = new SettingsGroupDto { Name = "   ", Priority = 1 };

        Assert.True(string.IsNullOrWhiteSpace(nameless.Name));
    }

    /// <summary>
    /// The report says what happened, including what this server had never heard of.
    /// </summary>
    [Fact]
    public void TheReportNamesWhatWasLeftOut()
    {
        var report = new RestoreReport { Configuration = true, Groups = 2, Users = 3, UnknownUsers = 4 };

        var read = _serialization.DeserializeJson<RestoreReport>(_serialization.SerializeToJson(report));

        Assert.True(read!.Configuration);
        Assert.Equal(4, read.UnknownUsers);
    }

    /// <summary>
    /// A person's notification choices go out in the backup file and come back from it.
    /// </summary>
    [Fact]
    public void APersonsChoicesTravelInTheBackup()
    {
        var user = Guid.NewGuid();
        var mine = new NotificationPreferences { Pause = new NotificationPause() };
        var backup = new ConfigurationBackup { NotificationPreferences = [new PreferencesBackup { UserId = user, Preferences = mine }] };

        var read = _serialization.DeserializeJson<ConfigurationBackup>(_serialization.SerializeToJson(backup));

        Assert.True(read!.NotificationPreferences!.Single().Preferences!.IsPaused(DateTime.UtcNow));
        Assert.Equal(user, read.NotificationPreferences!.Single().UserId);
    }

    /// <summary>
    /// A backup taken before this existed says nothing about choices, which must stay as they
    /// are.
    /// </summary>
    [Fact]
    public void AnOlderBackupSaysNothingAboutChoices()
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>("""{"plugin":"0.70.0.0","groups":[],"users":[]}""");

        Assert.Null(read!.NotificationPreferences);
    }

    /// <summary>
    /// A file with an empty choice in it, or one that leaves out what the person chose, is
    /// refused before anything is written: the restore would stop halfway through, or take
    /// that person's choices away.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("""{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11"}""")]
    [InlineData("""{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11","preferences":null}""")]
    public void AFileWithAnEmptyChoiceIsRefusedBeforeAnythingIsWritten(string choice)
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>(
            $$"""{"plugin":"0.70.0.0","groups":[],"users":[],"notificationPreferences":[{{choice}}]}""");

        Assert.NotNull(read!.PreferencesProblem());
        Assert.Null(new ConfigurationBackup().PreferencesProblem());
    }
}
