using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A home screen for one group of people and not for the others (P5.4).
/// </summary>
/// <remarks>
/// There is no separate mechanism for this and there should not be: the home layout is a
/// setting, and settings already resolve server, then groups in their order, then the
/// user. What was missing was anything saying so, which is what these are: a home is
/// worth proving on its own, because it is the one setting whose value is a document
/// rather than a number, and because an empty one and an absent one are different
/// answers.
/// </remarks>
public class HomePerGroupTests : IDisposable
{
    private readonly string _directory;
    private readonly PluginDatabase _db;
    private readonly SerializationHelper _serialization = new();
    private readonly SettingsResolutionService _resolution;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomePerGroupTests"/> class.
    /// </summary>
    public HomePerGroupTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
        _resolution = new SettingsResolutionService(_serialization);
    }

    private static Home Layout(params string[] titles)
    {
        var sections = new Section[titles.Length];

        for (var i = 0; i < titles.Length; i++)
        {
            sections[i] = new Section
            {
                title = titles[i],
                orientation = SectionOrientation.vertical,
                items = new Items { includeItemTypes = [BaseItemKind.Movie], limit = 10 }
            };
        }

        return new Home { sections = sections };
    }

    private static List<string> Titles(Section[] sections, Func<Section, string> title) =>
        [.. sections.Select(title)];

    private Settings Resolved(Guid userId, Settings global) =>
        _resolution.Resolve(global, _db.GetGroupsForUser(userId), _db.GetUserSettingsOverride(userId));

    /// <summary>
    /// A group's home reaches the people in it, and nobody else.
    /// </summary>
    [Fact]
    public void AGroupsHomeReachesThePeopleInIt()
    {
        var inside = Guid.NewGuid();
        var outside = Guid.NewGuid();

        var group = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Kids",
            Priority = 1,
            SettingsJson = _serialization.SerializeToJson(new Settings { home = new Lockable<Home> { value = Layout("Cartoons") } })
        });

        _db.SetGroupMembers(group.Id, [inside]);

        var global = new Settings { home = new Lockable<Home> { value = Layout("Continue Watching", "Latest") } };

        Assert.Equal(["Cartoons"], Titles(Resolved(inside, global).home!.value.sections!, section => section.title));
        Assert.Equal(["Continue Watching", "Latest"], Titles(Resolved(outside, global).home!.value.sections!, section => section.title));
    }

    /// <summary>
    /// The layers stack the way every other setting does: the higher priority group wins,
    /// and what is aimed at one person wins over both.
    /// </summary>
    [Fact]
    public void TheMostSpecificLayoutWins()
    {
        var userId = Guid.NewGuid();

        var lower = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Everyone on the sofa",
            Priority = 1,
            SettingsJson = _serialization.SerializeToJson(new Settings { home = new Lockable<Home> { value = Layout("Sofa") } })
        });

        var higher = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Night shift",
            Priority = 5,
            SettingsJson = _serialization.SerializeToJson(new Settings { home = new Lockable<Home> { value = Layout("Night") } })
        });

        _db.SetGroupMembers(lower.Id, [userId]);
        _db.SetGroupMembers(higher.Id, [userId]);

        var global = new Settings { home = new Lockable<Home> { value = Layout("Everyone") } };

        Assert.Equal(["Night"], Titles(Resolved(userId, global).home!.value.sections!, section => section.title));

        _db.SaveUserSettingsOverride(
            userId,
            _serialization.SerializeToJson(new Settings { home = new Lockable<Home> { value = Layout("Mine") } }),
            "{}");

        Assert.Equal(["Mine"], Titles(Resolved(userId, global).home!.value.sections!, section => section.title));
    }

    /// <summary>
    /// A group that says nothing about the home leaves the server's standing, and a group
    /// that says the home is empty is a different answer from one that says nothing.
    /// </summary>
    [Fact]
    public void SayingNothingAndSayingNoneAreDifferentAnswers()
    {
        var quiet = Guid.NewGuid();
        var bare = Guid.NewGuid();

        var saysNothing = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Says nothing",
            Priority = 1,
            SettingsJson = _serialization.SerializeToJson(new Settings { subtitleSize = new Lockable<int> { value = 60 } })
        });

        var saysNone = _db.SaveSettingsGroup(new SettingsGroup
        {
            Name = "Says none",
            Priority = 1,
            SettingsJson = _serialization.SerializeToJson(new Settings { home = new Lockable<Home> { value = new Home { sections = [] } } })
        });

        _db.SetGroupMembers(saysNothing.Id, [quiet]);
        _db.SetGroupMembers(saysNone.Id, [bare]);

        var global = new Settings { home = new Lockable<Home> { value = Layout("Everyone") } };

        Assert.Equal(["Everyone"], Titles(Resolved(quiet, global).home!.value.sections!, section => section.title));
        Assert.Empty(Resolved(bare, global).home!.value.sections!);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
