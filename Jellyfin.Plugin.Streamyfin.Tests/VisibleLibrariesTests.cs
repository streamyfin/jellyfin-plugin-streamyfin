using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A user is told only about the libraries they can open.
/// </summary>
/// <remarks>
/// Two leaks of the same kind. A home section built on a library a user may not open was
/// served to them all the same, so its title named that library (#69). And a new movie or
/// episode was announced to every registered device, whoever it belonged to, so its title
/// reached people who could not open it.
/// </remarks>
public class VisibleLibrariesTests
{
    private static readonly Guid Kids = Guid.Parse("5a3a9b0e0f7c4d2b9a1c6e8f0d2b4c6a");
    private static readonly Guid Private = Guid.Parse("c0ffee00-1234-4bcd-8ef0-1234567890ab");

    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SettingsResolutionService _resolution = new(new SerializationHelper());

    private static bool KidsOnly(Guid folder) => folder == Kids;

    private static bool NeverAsked(Guid folder) =>
        throw new InvalidOperationException($"{folder} should not have been looked up");

    private static Section LatestIn(string title, string? parentId) =>
        new() { title = title, latest = new Latest { parentId = parentId } };

    private static Section ItemsIn(string title, string? parentId) =>
        new() { title = title, items = new Items { parentId = parentId } };

    private static Section NextUpIn(string title, string? parentId) =>
        new() { title = title, nextUp = new NextUp { parentId = parentId } };

    private static Section CustomWith(string title, string key, string value)
    {
        var query = new SerializableDictionary<string, string>();
        query[key] = value;
        return new Section { title = title, custom = new CustomEndpoint { endpoint = "/Items", query = query } };
    }

    private static Settings HomeOf(params Section[] sections) => new()
    {
        home = new Lockable<Home> { value = new Home { sections = sections } }
    };

    private static string[] Titles(Settings? settings) =>
        settings?.home?.value?.sections?.Select(section => section.title).ToArray() ?? [];

    /// <summary>
    /// A section built on a library the user cannot open is not served, and the name
    /// its title carries goes with it.
    /// </summary>
    [Fact]
    public void ASectionOnALibraryTheUserCannotOpenIsNotServed()
    {
        var settings = HomeOf(
            LatestIn("Recently added in Kids", Kids.ToString("N")),
            LatestIn("Recently added in Private", Private.ToString("N")));

        Sections.KeepVisible(settings, KidsOnly);

        Assert.Equal(new[] { "Recently added in Kids" }, Titles(settings));
    }

    /// <summary>
    /// Every payload that can name a library is read, the custom query included.
    /// </summary>
    [Fact]
    public void EveryPayloadThatNamesALibraryIsChecked()
    {
        var hidden = Private.ToString("N");
        var settings = HomeOf(
            ItemsIn("items", hidden),
            NextUpIn("next up", hidden),
            LatestIn("latest", hidden),
            CustomWith("custom", "ParentId", hidden),
            ItemsIn("everything", null));

        Sections.KeepVisible(settings, KidsOnly);

        Assert.Equal(new[] { "everything" }, Titles(settings));
    }

    /// <summary>
    /// A section that names no library is served without anything being looked up.
    /// </summary>
    [Fact]
    public void ASectionThatNamesNoLibraryIsLeftAlone()
    {
        var settings = HomeOf(
            ItemsIn("items", null),
            new Section { title = "custom", custom = new CustomEndpoint { endpoint = "/Shows/NextUp" } },
            CustomWith("filtered by genre", "Genres", "Animation"));

        Sections.KeepVisible(settings, NeverAsked);

        Assert.Equal(new[] { "items", "custom", "filtered by genre" }, Titles(settings));
    }

    /// <summary>
    /// A value that is not an id names nothing that can be checked, and is left to the
    /// request that uses it, as before.
    /// </summary>
    [Fact]
    public void AValueThatIsNotAnIdIsLeftAlone()
    {
        var settings = HomeOf(LatestIn("typo", "not-an-id"));

        Sections.KeepVisible(settings, NeverAsked);

        Assert.Equal(new[] { "typo" }, Titles(settings));
    }

    /// <summary>
    /// Jellyfin writes an id with or without dashes, and a query parameter in any case.
    /// </summary>
    [Fact]
    public void EitherSpellingIsRead()
    {
        var settings = HomeOf(
            LatestIn("with dashes", Private.ToString("D")),
            CustomWith("lower case key", "parentid", Private.ToString("N")));

        Sections.KeepVisible(settings, KidsOnly);

        Assert.Empty(Titles(settings));
    }

    /// <summary>
    /// A custom endpoint can carry its parameters in its own address, which the app sends
    /// as written, so that query is read too.
    /// </summary>
    [Fact]
    public void AnIdInTheEndpointsOwnAddressIsRead()
    {
        var settings = HomeOf(
            new Section { title = "hidden", custom = new CustomEndpoint { endpoint = $"/Items?Recursive=true&ParentId={Private:N}" } },
            new Section { title = "shared", custom = new CustomEndpoint { endpoint = $"/Items?parentid={Kids:N}" } },
            new Section { title = "no query", custom = new CustomEndpoint { endpoint = "/Items/Latest" } });

        Sections.KeepVisible(settings, KidsOnly);

        Assert.Equal(new[] { "shared", "no query" }, Titles(settings));
    }

    /// <summary>
    /// A configuration with no home, or a home with no sections, is not a failure.
    /// </summary>
    [Fact]
    public void NothingToFilterIsNotAFailure()
    {
        Sections.KeepVisible(null, KidsOnly);
        Sections.KeepVisible(new Settings(), KidsOnly);
        Sections.KeepVisible(new Settings { home = new Lockable<Home> { value = new Home() } }, KidsOnly);
    }

    /// <summary>
    /// The configuration the server holds is not touched. Resolution hands the stored
    /// sections through by reference, so a filter that removed them in place would take
    /// them from every later caller and from the admin page, which saves what it loads.
    /// </summary>
    [Fact]
    public void TheStoredConfigurationIsNotTouched()
    {
        var stored = new Config
        {
            settings = HomeOf(
                LatestIn("Kids", Kids.ToString("N")),
                LatestIn("Private", Private.ToString("N")),
                ItemsIn("Everything", null))
        };

        var served = _resolution.ForCaller(stored, null, null, isElevated: false, canOpen: KidsOnly);

        Assert.Equal(new[] { "Kids", "Everything" }, Titles(served.settings));
        Assert.Equal(new[] { "Kids", "Private", "Everything" }, Titles(stored.settings));
        Assert.Equal(new int?[] { 0, 1, 2 }, stored.settings!.home!.value!.sections!.Select(s => s.order));

        var everyone = _resolution.ForCaller(stored, null, null, isElevated: false, canOpen: _ => true);
        Assert.Equal(new[] { "Kids", "Private", "Everything" }, Titles(everyone.settings));
    }

    /// <summary>
    /// What is served keeps the order the administrator set, with each section still
    /// carrying the position it has for everyone.
    /// </summary>
    [Fact]
    public void WhatIsLeftKeepsItsOrder()
    {
        var resolved = _resolution.Resolve(
            HomeOf(
                LatestIn("first", Kids.ToString("N")),
                LatestIn("hidden", Private.ToString("N")),
                ItemsIn("third", null)),
            null,
            null,
            KidsOnly);

        Assert.Equal(new[] { "first", "third" }, Titles(resolved));
        Assert.Equal(new int?[] { 0, 2 }, resolved.home!.value!.sections!.Select(s => s.order));
    }

    /// <summary>
    /// An administrator editing the configuration is handed every section, since the
    /// page saves what it loads.
    /// </summary>
    [Fact]
    public void AnAdministratorEditingTheConfigurationIsHandedEverySection()
    {
        var stored = new Config
        {
            settings = HomeOf(LatestIn("Kids", Kids.ToString("N")), LatestIn("Private", Private.ToString("N")))
        };

        var served = _resolution.ForCaller(stored, null, null, isElevated: true, canOpen: KidsOnly);

        Assert.Equal(new[] { "Kids", "Private" }, Titles(served.settings));
    }

    /// <summary>
    /// A new item is announced to the devices of the users who can open it, and to no
    /// one else.
    /// </summary>
    [Fact]
    public void ANewItemIsAnnouncedOnlyToWhoCanOpenIt()
    {
        var tokens = new[]
        {
            Device(Alice, "alice-phone"),
            Device(Alice, "alice-tablet"),
            Device(Bob, "bob-phone")
        };

        var recipients = NotificationHelper.RecipientsWho(tokens, user => user == Alice);

        Assert.Equal(new[] { "alice-phone", "alice-tablet" }, recipients);
    }

    /// <summary>
    /// Each user is asked about once, however many devices they have.
    /// </summary>
    [Fact]
    public void EachUserIsAskedAboutOnce()
    {
        var asked = new List<Guid>();
        var tokens = new[] { Device(Alice, "a"), Device(Alice, "b"), Device(Alice, "c"), Device(Bob, "d") };

        NotificationHelper.RecipientsWho(tokens, user =>
        {
            asked.Add(user);
            return true;
        });

        Assert.Equal(new[] { Alice, Bob }, asked);
    }

    /// <summary>
    /// A token registered on two rows is sent to once.
    /// </summary>
    [Fact]
    public void ATokenOnTwoRowsGoesOnce()
    {
        var tokens = new[] { Device(Alice, "same"), Device(Alice, "same") };

        Assert.Equal(new[] { "same" }, NotificationHelper.RecipientsWho(tokens, _ => true));
    }

    /// <summary>
    /// Nobody allowed means nobody notified, rather than everybody.
    /// </summary>
    [Fact]
    public void NobodyAllowedMeansNobodyNotified()
    {
        var tokens = new[] { Device(Alice, "a"), Device(Bob, "b") };

        Assert.Empty(NotificationHelper.RecipientsWho(tokens, _ => false));
    }

    /// <summary>
    /// A disabled account is told nothing, even about what it could open. Jellyfin refuses
    /// every request such an account makes, and its devices are still registered.
    /// </summary>
    [Fact]
    public void ADisabledAccountIsToldNothing()
    {
        var disabled = Account();
        disabled.Permissions.Add(new Permission(PermissionKind.IsDisabled, true));

        Assert.False(NotificationHelper.MayBeTold(disabled, _ => true));
    }

    /// <summary>
    /// An account that is not disabled is told what it can open, and only that.
    /// </summary>
    [Fact]
    public void AnEnabledAccountIsToldWhatItCanOpen()
    {
        var enabled = Account();
        enabled.Permissions.Add(new Permission(PermissionKind.IsDisabled, false));

        Assert.True(NotificationHelper.MayBeTold(enabled, _ => true));
        Assert.False(NotificationHelper.MayBeTold(enabled, _ => false));
    }

    /// <summary>
    /// A device whose account is gone is told nothing.
    /// </summary>
    [Fact]
    public void AnAccountThatIsGoneIsToldNothing()
    {
        Assert.False(NotificationHelper.MayBeTold(null, _ => true));
    }

    private static User Account() => new("zz-test", "provider", "reset");

    private static DeviceToken Device(Guid user, string token) => new()
    {
        DeviceId = Guid.NewGuid(),
        UserId = user,
        Token = token
    };
}
