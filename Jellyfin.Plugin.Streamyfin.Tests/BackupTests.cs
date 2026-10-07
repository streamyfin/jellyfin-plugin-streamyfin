using System;
using System.Collections.Generic;
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

    /// <summary>
    /// A file with the same person twice is refused before anything is written: a restore can
    /// keep only one of the two, and stopped with an error on the second.
    /// </summary>
    [Fact]
    public void AFileWithOnePersonTwiceIsRefused()
    {
        var person = Guid.NewGuid();
        var backup = new ConfigurationBackup
        {
            NotificationPreferences =
            [
                new PreferencesBackup { UserId = person, Preferences = new NotificationPreferences() },
                new PreferencesBackup { UserId = person, Preferences = new NotificationPreferences { Pause = new NotificationPause() } }
            ]
        };

        Assert.NotNull(backup.PreferencesProblem());
    }

    /// <summary>
    /// A file is held to the same most as the app: every send reads everyone's choices, so a
    /// restore brings in no list longer than a person could have made.
    /// </summary>
    [Theory]
    [InlineData(MyNotifications.MostMutedShows, MyNotifications.MostMutedLibraries, false)]
    [InlineData(MyNotifications.MostMutedShows + 1, 0, true)]
    [InlineData(0, MyNotifications.MostMutedLibraries + 1, true)]
    public void AFileWithMoreThanTheMostIsRefused(int shows, int libraries, bool refused)
    {
        var backup = new ConfigurationBackup
        {
            NotificationPreferences =
            [
                new PreferencesBackup
                {
                    UserId = Guid.NewGuid(),
                    Preferences = new NotificationPreferences
                    {
                        MutedShows = [.. Enumerable.Range(0, shows).Select(_ => Guid.NewGuid())],
                        MutedLibraries = [.. Enumerable.Range(0, libraries).Select(_ => Guid.NewGuid())]
                    }
                }
            ]
        };

        Assert.Equal(refused, backup.PreferencesProblem() is not null);
    }

    /// <summary>
    /// A file the restore could only apply halfway is refused before anything is written. It
    /// writes groups back by id and by name, which are unique, and user settings by user, so
    /// a repeat or an empty entry stopped it with an error after the configuration was saved
    /// (#228). A group with no name is refused the way one written through the API is.
    /// </summary>
    [Theory]
    [InlineData("""{"groups":[{"id":"6f0c1c55-8e2b-4c44-9d65-0a1f2c3d4e5f","name":"Kids"},{"id":"6f0c1c55-8e2b-4c44-9d65-0a1f2c3d4e5f","name":"Teens"}],"users":[]}""")]
    [InlineData("""{"groups":[{"name":"Kids"},{"name":"Kids"}],"users":[]}""")]
    [InlineData("""{"groups":[],"users":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11"},{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11"}]}""")]
    [InlineData("""{"groups":[null],"users":[]}""")]
    [InlineData("""{"groups":[],"users":[null]}""")]
    [InlineData("""{"groups":[{"name":" "}],"users":[]}""")]
    public void AFileTheRestoreCouldOnlyApplyHalfwayIsRefused(string file)
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>(file);

        Assert.NotNull(read!.TargetingProblem());
    }

    /// <summary>
    /// Groups without an id each get a new one, and names that differ only by case are two
    /// groups the server can hold, so neither is refused.
    /// </summary>
    [Fact]
    public void GroupsWithoutAnIdOrNamedApartByCaseAreRestored()
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>(
            """{"groups":[{"name":"Kids"},{"name":"kids"}],"users":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11"}]}""");

        Assert.Null(read!.TargetingProblem());
    }

    /// <summary>
    /// What a group or a user says about the events is part of what an administrator set,
    /// so a backup carries it at both levels.
    /// </summary>
    [Fact]
    public void ABackupCarriesWhatEachLevelSaysAboutTheEvents()
    {
        var backup = new ConfigurationBackup
        {
            Groups =
            [
                new SettingsGroupDto
                {
                    Name = "Night shift",
                    Notifications = new() { ["taskFailed"] = new NotificationTargeting { Enabled = true } }
                }
            ],
            Users =
            [
                new UserBackup
                {
                    UserId = Guid.NewGuid(),
                    Notifications = new() { ["taskFailed"] = new NotificationTargeting { Enabled = false } }
                }
            ]
        };

        var read = _serialization.DeserializeJson<ConfigurationBackup>(_serialization.SerializeToJson(backup));

        Assert.True(Assert.Single(read!.Groups).Notifications?["taskFailed"].Enabled);
        Assert.False(Assert.Single(read.Users).Notifications?["taskFailed"].Enabled);
    }

    /// <summary>
    /// The rows a restore writes keep what each level says about the events next to its
    /// settings, and leave out the members and users this server does not have. The events
    /// were left out, so a restore took every group's and every user's notifications away.
    /// </summary>
    [Fact]
    public void TheRowsARestoreWritesKeepWhatEachLevelSaysAboutTheEvents()
    {
        var known = Guid.NewGuid();
        var backup = new ConfigurationBackup
        {
            Groups =
            [
                new SettingsGroupDto
                {
                    Name = "Night shift",
                    UserIds = [known, Guid.NewGuid()],
                    Notifications = new() { ["taskFailed"] = new NotificationTargeting { Enabled = true } }
                }
            ],
            Users =
            [
                new UserBackup
                {
                    UserId = known,
                    Notifications = new() { ["taskFailed"] = new NotificationTargeting { Enabled = false } }
                },
                new UserBackup { UserId = Guid.NewGuid() }
            ]
        };

        var rows = backup.ToRows(_serialization, new HashSet<Guid> { known });

        var (group, members) = Assert.Single(rows.Groups);
        Assert.Equal("""{"taskFailed":{"enabled":true}}""", group.NotificationsJson);
        Assert.Equal(known, Assert.Single(members));
        var user = Assert.Single(rows.Users);
        Assert.Equal(known, user.UserId);
        Assert.Equal("""{"taskFailed":{"enabled":false}}""", user.NotificationsJson);
        Assert.Equal(1, rows.UnknownMembers);
        Assert.Equal(1, rows.UnknownUsers);
    }

    /// <summary>
    /// An event this server does not have is refused in a file the way it is on the pages,
    /// and the sentence says which level names it.
    /// </summary>
    /// <param name="file">The backup.</param>
    /// <param name="level">What the sentence has to name.</param>
    [Theory]
    [InlineData("""{"groups":[{"name":"Night shift","notifications":{"taskFaild":{"enabled":true}}}],"users":[]}""", "Night shift")]
    [InlineData("""{"groups":[],"users":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11","notifications":{"taskFaild":{"enabled":true}}}]}""", "4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11")]
    public void AnEventThisServerDoesNotHaveIsNotRestored(string file, string level)
    {
        var problem = _serialization.DeserializeJson<ConfigurationBackup>(file)!.NotificationsProblem(Known);

        Assert.NotNull(problem);
        Assert.Contains(level, problem, StringComparison.Ordinal);
        Assert.Contains("taskFaild", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file whose levels name events this server has, or say nothing about the events, as
    /// every backup taken before they could does, has nothing to refuse.
    /// </summary>
    /// <param name="file">The backup.</param>
    [Theory]
    [InlineData("""{"groups":[{"name":"Night shift","notifications":{"taskFailed":{"enabled":true}}}],"users":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11","notifications":{"taskFailed":{"enabled":false}}}]}""")]
    [InlineData("""{"groups":[{"name":"Kids"}],"users":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11"}]}""")]
    public void WhatALevelSaysAboutKnownEventsIsRestored(string file)
    {
        Assert.Null(_serialization.DeserializeJson<ConfigurationBackup>(file)!.NotificationsProblem(Known));
    }

    /// <summary>
    /// A user this server does not have is left out of a restore, so what they say about the
    /// events is not checked either: a file from another server is not refused over a user it
    /// would not write.
    /// </summary>
    [Fact]
    public void AUserThisServerDoesNotHaveIsNotChecked()
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>(
            """{"groups":[],"users":[{"userId":"9a3e1f2b-7c4d-4e5f-8a6b-1c2d3e4f5a6b","notifications":{"taskFaild":{"enabled":true}}}]}""");

        Assert.Null(read!.NotificationsProblem(Known));
    }

    // The user the files above name, as the server they are restored on knows them.
    private static readonly HashSet<Guid> Known = [Guid.Parse("4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11")];

    /// <summary>
    /// A backup carries the titles each person waits for, and puts them back as they were
    /// (#225).
    /// </summary>
    [Fact]
    public void ABackupCarriesTheTitlesEachPersonWaitsFor()
    {
        var alice = Guid.NewGuid();
        var arrived = Guid.NewGuid();
        var backup = new ConfigurationBackup
        {
            Awaited =
            [
                AwaitedTitleBackup.From(new Db.AwaitedTitle
                {
                    UserId = alice,
                    MediaType = "tv",
                    TmdbId = 1399,
                    TvdbId = 121361,
                    Title = "Game of Thrones",
                    Year = 2011,
                    AddedAt = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc),
                    ArrivedItemId = arrived
                })
            ]
        };

        var read = _serialization.DeserializeJson<ConfigurationBackup>(_serialization.SerializeToJson(backup));
        var row = Assert.Single(read!.Awaited!).ToRow();

        Assert.Equal(alice, row.UserId);
        Assert.Equal(121361, row.TvdbId);
        Assert.Equal("Game of Thrones", row.Title);
        Assert.Equal(arrived, row.ArrivedItemId);
        Assert.Null(read.AwaitedProblem());
    }

    /// <summary>
    /// An awaited title the restore could not keep refuses the file before anything is
    /// written: an empty entry, a title that is not one, or the same title twice for a person.
    /// </summary>
    /// <param name="file">The backup.</param>
    [Theory]
    [InlineData("""{"groups":[],"users":[],"awaitedTitles":[null]}""")]
    [InlineData("""{"groups":[],"users":[],"awaitedTitles":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11","mediaType":"music","tmdbId":1,"title":"Album"}]}""")]
    [InlineData("""{"groups":[],"users":[],"awaitedTitles":[{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11","mediaType":"movie","tmdbId":603,"title":"The Matrix"},{"userId":"4c1ee5d4-5e8f-4f3b-9d0a-2b6a1f0e8c11","mediaType":"movie","tmdbId":603,"title":"The Matrix"}]}""")]
    public void AnAwaitedTitleTheRestoreCouldNotKeepIsRefused(string file) =>
        Assert.NotNull(_serialization.DeserializeJson<ConfigurationBackup>(file)!.AwaitedProblem());

    /// <summary>
    /// A backup taken before people could wait for titles says nothing about them, and a
    /// restore leaves them as they are.
    /// </summary>
    [Fact]
    public void AnOlderBackupSaysNothingAboutAwaitedTitles()
    {
        var read = _serialization.DeserializeJson<ConfigurationBackup>("""{"groups":[],"users":[]}""");

        Assert.Null(read!.Awaited);
        Assert.Null(read.AwaitedProblem());
    }
}
