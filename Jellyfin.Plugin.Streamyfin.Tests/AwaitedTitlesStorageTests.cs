using System;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Storing the titles people wait for, and settling an arrival in one step (#225).
/// </summary>
public class AwaitedTitlesStorageTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _directory;
    private readonly PluginDatabase _db;
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the <see cref="AwaitedTitlesStorageTests"/> class.
    /// </summary>
    public AwaitedTitlesStorageTests()
    {
        _directory = TestDirectory.Create();
        _db = new PluginDatabase(_directory);
    }

    private static AwaitedTitle Matrix(Guid userId, DateTime? addedAt = null) => new()
    {
        UserId = userId,
        MediaType = AwaitedTitles.Movie,
        TmdbId = 603,
        Title = "The Matrix",
        Year = 1999,
        AddedAt = addedAt ?? Now
    };

    private static AwaitedTitle Thrones(Guid userId) => new()
    {
        UserId = userId,
        MediaType = AwaitedTitles.Tv,
        TmdbId = 1399,
        TvdbId = 121361,
        Title = "Game of Thrones",
        AddedAt = Now
    };

    /// <summary>
    /// A title added is listed, the latest first.
    /// </summary>
    [Fact]
    public void ATitleAddedIsListedTheLatestFirst()
    {
        Assert.Equal(AwaitResult.Added, _db.AddAwaitedTitle(Matrix(_alice, Now.AddHours(-1))));
        Assert.Equal(AwaitResult.Added, _db.AddAwaitedTitle(Thrones(_alice)));

        Assert.Equal([1399, 603], _db.GetAwaitedTitles(_alice).Select(title => title.TmdbId));
        Assert.Empty(_db.GetAwaitedTitles(_bob));
    }

    /// <summary>
    /// The same title twice is kept once.
    /// </summary>
    [Fact]
    public void TheSameTitleTwiceIsKeptOnce()
    {
        _db.AddAwaitedTitle(Matrix(_alice));

        Assert.Equal(AwaitResult.AlreadyAwaited, _db.AddAwaitedTitle(Matrix(_alice)));
        Assert.Single(_db.GetAwaitedTitles(_alice));
    }

    /// <summary>
    /// A full list takes no more.
    /// </summary>
    [Fact]
    public void AFullListTakesNoMore()
    {
        _db.ReplaceAwaitedTitles(Enumerable.Range(1, AwaitedTitles.MostAwaited).Select(id => new AwaitedTitle
        {
            UserId = _alice,
            MediaType = AwaitedTitles.Movie,
            TmdbId = id,
            Title = $"Movie {id}",
            AddedAt = Now
        }));

        Assert.Equal(AwaitResult.Full, _db.AddAwaitedTitle(Thrones(_alice)));
        Assert.Equal(AwaitResult.Added, _db.AddAwaitedTitle(Thrones(_bob)));
    }

    /// <summary>
    /// Removing a title takes it off that person's list only.
    /// </summary>
    [Fact]
    public void RemovingATitleTakesItOffThatPersonsListOnly()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        _db.AddAwaitedTitle(Matrix(_bob));

        Assert.True(_db.RemoveAwaitedTitle(_alice, AwaitedTitles.Movie, 603));
        Assert.False(_db.RemoveAwaitedTitle(_alice, AwaitedTitles.Movie, 603));
        Assert.Empty(_db.GetAwaitedTitles(_alice));
        Assert.Single(_db.GetAwaitedTitles(_bob));
    }

    /// <summary>
    /// Everyone who waited is told, once: a second arrival of the same title, the next
    /// episode of a season that came in one go, finds nobody waiting.
    /// </summary>
    [Fact]
    public void AnArrivalIsAnnouncedOnce()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        _db.AddAwaitedTitle(Matrix(_bob));
        var item = Guid.NewGuid();

        var first = _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);
        var second = _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
        Assert.Empty(_db.AllAwaitedTitles());
    }

    /// <summary>
    /// A show Jellyfin only knows by its TVDB id is found by it.
    /// </summary>
    [Fact]
    public void AShowIsFoundByItsTvdbIdToo()
    {
        _db.AddAwaitedTitle(Thrones(_alice));

        var told = _db.SettleArrival(AwaitedTitles.Tv, null, 121361, Guid.NewGuid(), _ => AwaitedOutcome.Announce);

        Assert.Equal(_alice, Assert.Single(told).UserId);
    }

    /// <summary>
    /// A movie with the id of an awaited show is not that show.
    /// </summary>
    [Fact]
    public void AMovieWithTheIdOfAShowIsNotThatShow()
    {
        _db.AddAwaitedTitle(Thrones(_alice));

        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, 1399, null, Guid.NewGuid(), _ => AwaitedOutcome.Announce));
        Assert.Single(_db.GetAwaitedTitles(_alice));
    }

    /// <summary>
    /// An arrival held by a pause stays listed as arrived, is not matched again, and is told
    /// once the pause is over.
    /// </summary>
    [Fact]
    public void AHeldArrivalWaitsForThePause()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var item = Guid.NewGuid();

        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Hold));
        Assert.Equal(item, Assert.Single(_db.GetAwaitedTitles(_alice)).ArrivedItemId);
        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, 603, null, Guid.NewGuid(), _ => AwaitedOutcome.Announce));

        Assert.Empty(_db.SettleHeld(_ => AwaitedOutcome.Hold));
        var told = _db.SettleHeld(_ => AwaitedOutcome.Announce);

        Assert.Equal(item, Assert.Single(told).ArrivedItemId);
        Assert.Empty(_db.GetAwaitedTitles(_alice));
    }

    /// <summary>
    /// Somebody who cannot open what arrived keeps waiting, and a later copy finds them.
    /// </summary>
    [Fact]
    public void SomebodyWhoCannotOpenItKeepsWaiting()
    {
        _db.AddAwaitedTitle(Matrix(_alice));

        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, 603, null, Guid.NewGuid(), _ => AwaitedOutcome.KeepWaiting));
        Assert.Null(Assert.Single(_db.GetAwaitedTitles(_alice)).ArrivedItemId);
        Assert.Single(_db.SettleArrival(AwaitedTitles.Movie, 603, null, Guid.NewGuid(), _ => AwaitedOutcome.Announce));
    }

    /// <summary>
    /// A title nothing would announce is not kept.
    /// </summary>
    [Fact]
    public void ATitleNothingWouldAnnounceIsNotKept()
    {
        _db.AddAwaitedTitle(Matrix(_alice));

        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, 603, null, Guid.NewGuid(), _ => AwaitedOutcome.Drop));
        Assert.Empty(_db.GetAwaitedTitles(_alice));
    }

    /// <summary>
    /// An item without any id settles nothing.
    /// </summary>
    [Fact]
    public void AnItemWithoutAnyIdSettlesNothing()
    {
        _db.AddAwaitedTitle(Matrix(_alice));

        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, null, null, Guid.NewGuid(), _ => AwaitedOutcome.Announce));
        Assert.Single(_db.GetAwaitedTitles(_alice));
    }

    /// <summary>
    /// Replacing the titles puts back exactly what a backup holds.
    /// </summary>
    [Fact]
    public void ReplacingTheTitlesPutsBackWhatABackupHolds()
    {
        _db.AddAwaitedTitle(Matrix(_alice));

        _db.ReplaceAwaitedTitles([Thrones(_bob)]);

        Assert.Equal(_bob, Assert.Single(_db.AllAwaitedTitles()).UserId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
