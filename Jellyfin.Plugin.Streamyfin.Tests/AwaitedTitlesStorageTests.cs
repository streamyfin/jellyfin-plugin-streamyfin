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

    // Who the new item message about the Matrix, this item, leaves out: copies of it count for
    // the hour before, the same item since the moment given.
    private System.Collections.Generic.HashSet<Guid> LeftOutOfMatrix(Guid item, DateTime since) =>
        _db.WhoWaitedFor(AwaitedTitles.Movie, 603, null, item, since, DateTime.UtcNow.AddHours(-1));

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

    /// <summary>
    /// A title announced stays as a record of who was told about what arrived, so the new item
    /// message can leave them out (#289), and is listed nowhere: not to the person, not in a
    /// backup, and never announced again.
    /// </summary>
    [Fact]
    public void AnAnnouncedTitleIsKeptAsToldAndListedNowhere()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        _db.AddAwaitedTitle(Matrix(_bob));
        var item = Guid.NewGuid();

        _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        Assert.Equal(new[] { _alice, _bob }.Order(), LeftOutOfMatrix(item, DateTime.UtcNow.AddMinutes(-1)).Order());
        Assert.Empty(_db.WhoWaitedFor(AwaitedTitles.Movie, 604, null, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(-1)));
        Assert.Empty(_db.GetAwaitedTitles(_alice));
        Assert.Empty(_db.AllAwaitedTitles());
        Assert.Empty(_db.SettleHeld(_ => AwaitedOutcome.Announce));
    }

    /// <summary>
    /// Told about a show before a batch of its episodes began, a person gets that batch: it is
    /// more of the show, which reaches them like anyone.
    /// </summary>
    [Fact]
    public void ATellingBeforeTheBatchBeganDoesNotCount()
    {
        _db.AddAwaitedTitle(Thrones(_alice));
        var show = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Tv, 1399, 121361, show, _ => AwaitedOutcome.Announce);

        Assert.Empty(_db.WhoWaitedFor(AwaitedTitles.Tv, 1399, 121361, show, DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow.AddHours(-1)));
    }

    /// <summary>
    /// A person a pause holds the arrival back from is left out too: the task tells them once
    /// the pause is over, and that is their one message, even when the pause ends before the
    /// batch of episodes goes.
    /// </summary>
    /// <remarks>
    /// Found by the review of #294. Only the people already told were left out, so a pause
    /// that ended within the batch's minute let the batch through, and the task then sent the
    /// awaited title message as well.
    /// </remarks>
    [Fact]
    public void AnArrivalAPauseHoldsIsLeftOutUntilItIsTold()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var item = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Hold);

        Assert.Equal(_alice, Assert.Single(LeftOutOfMatrix(item, DateTime.UtcNow.AddMinutes(1))));

        _db.SettleHeld(_ => AwaitedOutcome.Announce);

        Assert.Equal(_alice, Assert.Single(LeftOutOfMatrix(item, DateTime.UtcNow.AddMinutes(-1))));
    }

    /// <summary>
    /// Told about one copy of a title, a person is left out of the new item message about
    /// another copy, a 4K library beside the main one, within the hour.
    /// </summary>
    /// <remarks>
    /// Found by the review of #294. Who was told was matched by the item, and each copy is an
    /// item of its own: the second copy sent "Movie added" to the person "Now on the server"
    /// had just told.
    /// </remarks>
    [Fact]
    public void ACopyOfATitleToldAboutWithinTheHourIsLeftOut()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var first = Guid.NewGuid();
        var copy = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, first, _ => AwaitedOutcome.Announce);

        Assert.Empty(_db.SettleArrival(AwaitedTitles.Movie, 603, null, copy, _ => AwaitedOutcome.Announce));
        Assert.Equal(_alice, Assert.Single(_db.WhoWaitedFor(AwaitedTitles.Movie, 603, null, copy, DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow.AddHours(-1))));
        Assert.Empty(_db.WhoWaitedFor(AwaitedTitles.Movie, 603, null, copy, DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow.AddMinutes(1)));
    }

    /// <summary>
    /// A show is matched by its TVDB id alone, the one many shows in Jellyfin carry, and a
    /// movie is never matched by a show's ids.
    /// </summary>
    [Fact]
    public void WhoWaitedIsMatchedByTheTitlesKindAndIds()
    {
        _db.AddAwaitedTitle(Thrones(_alice));
        var show = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Tv, 1399, 121361, show, _ => AwaitedOutcome.Announce);
        var since = DateTime.UtcNow.AddMinutes(-1);
        var copies = DateTime.UtcNow.AddHours(-1);

        Assert.Equal(_alice, Assert.Single(_db.WhoWaitedFor(AwaitedTitles.Tv, null, 121361, show, since, copies)));
        Assert.Empty(_db.WhoWaitedFor(AwaitedTitles.Movie, 1399, null, show, since, copies));
        Assert.Empty(_db.WhoWaitedFor(AwaitedTitles.Tv, null, null, show, since, copies));
    }

    /// <summary>
    /// When the message telling them never left, the arrival is put back as held: the task
    /// tells them at its next run, and the new item message, which left them out, does not
    /// leave them with nothing.
    /// </summary>
    /// <remarks>
    /// Found by the review of #294. The row was kept as told before the message went, so a send
    /// Expo refused or never answered told the person nothing, while the new item message left
    /// them out.
    /// </remarks>
    [Fact]
    public void AMessageThatNeverLeftPutsTheArrivalBackAsHeld()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var item = Guid.NewGuid();
        var told = _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        Assert.Equal(1, _db.Untell(told));

        Assert.Equal(_alice, Assert.Single(LeftOutOfMatrix(item, DateTime.UtcNow.AddMinutes(1))));
        var again = Assert.Single(_db.SettleHeld(_ => AwaitedOutcome.Announce));
        Assert.Equal(item, again.ArrivedItemId);
    }

    /// <summary>
    /// Putting an arrival back as held leaves a later telling alone: only the one whose message
    /// never left is undone.
    /// </summary>
    [Fact]
    public void PuttingBackLeavesALaterTellingAlone()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var item = Guid.NewGuid();
        var first = _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);
        _db.Untell(first);
        // Told again a moment later, by the task's next run: a telling of its own.
        System.Threading.Thread.Sleep(20);
        _db.SettleHeld(_ => AwaitedOutcome.Announce);

        Assert.Equal(0, _db.Untell(first));
        Assert.Empty(_db.SettleHeld(_ => AwaitedOutcome.Announce));
    }

    /// <summary>
    /// Putting an arrival back as held leaves a fresh wait for the same title alone.
    /// </summary>
    [Fact]
    public void PuttingBackLeavesAFreshWaitAlone()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var told = _db.SettleArrival(AwaitedTitles.Movie, 603, null, Guid.NewGuid(), _ => AwaitedOutcome.Announce);
        _db.AddAwaitedTitle(Matrix(_alice, Now.AddDays(1)));

        Assert.Equal(0, _db.Untell(told));
        Assert.Null(Assert.Single(_db.GetAwaitedTitles(_alice)).ArrivedItemId);
        Assert.Single(_db.AllAwaitedTitles());
    }

    /// <summary>
    /// Waiting again for a title one was told about starts a fresh wait: the title went away
    /// and is awaited again.
    /// </summary>
    [Fact]
    public void WaitingAgainForATitleToldAboutStartsAFreshWait()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var item = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        Assert.Equal(AwaitResult.Added, _db.AddAwaitedTitle(Matrix(_alice, Now.AddDays(1))));

        var again = Assert.Single(_db.GetAwaitedTitles(_alice));
        Assert.Null(again.ArrivedItemId);
        Assert.Null(again.AnnouncedAt);
        Assert.Empty(LeftOutOfMatrix(item, DateTime.UtcNow.AddMinutes(-1)));
    }

    /// <summary>
    /// Titles one was told about do not fill the list.
    /// </summary>
    [Fact]
    public void TitlesToldAboutDoNotFillTheList()
    {
        for (var i = 0; i < AwaitedTitles.MostAwaited; i++)
        {
            _db.AddAwaitedTitle(new AwaitedTitle { UserId = _alice, MediaType = AwaitedTitles.Movie, TmdbId = 1000 + i, Title = $"Movie {i}", AddedAt = Now });
        }

        _db.SettleArrival(AwaitedTitles.Movie, 1000, null, Guid.NewGuid(), _ => AwaitedOutcome.Announce);

        Assert.Equal(AwaitResult.Added, _db.AddAwaitedTitle(Matrix(_alice)));
    }

    /// <summary>
    /// Removing a title takes off a wait, not the record of who was told.
    /// </summary>
    [Fact]
    public void RemovingATitleLeavesWhoWasToldAlone()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var item = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        Assert.False(_db.RemoveAwaitedTitle(_alice, AwaitedTitles.Movie, 603));
        Assert.Single(LeftOutOfMatrix(item, DateTime.UtcNow.AddMinutes(-1)));
    }

    /// <summary>
    /// The record of who was told is forgotten after a while, and only that.
    /// </summary>
    [Fact]
    public void WhoWasToldIsForgottenAfterAWhile()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        _db.AddAwaitedTitle(Thrones(_alice));
        var item = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        Assert.Equal(0, _db.ForgetAnnouncedBefore(DateTime.UtcNow.AddMinutes(-1)));
        Assert.Single(LeftOutOfMatrix(item, DateTime.MinValue));

        Assert.Equal(1, _db.ForgetAnnouncedBefore(DateTime.UtcNow.AddMinutes(1)));
        Assert.Empty(LeftOutOfMatrix(item, DateTime.MinValue));
        Assert.Single(_db.GetAwaitedTitles(_alice));
    }

    /// <summary>
    /// A restore puts back the waits a backup holds and keeps who was just told, except where
    /// the backup waits for that same title again.
    /// </summary>
    [Fact]
    public void ARestoreKeepsWhoWasJustTold()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        _db.AddAwaitedTitle(Matrix(_bob));
        var item = Guid.NewGuid();
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, item, _ => AwaitedOutcome.Announce);

        _db.ReplaceAwaitedTitles([Thrones(_bob), Matrix(_bob)]);

        Assert.Equal(_alice, Assert.Single(LeftOutOfMatrix(item, DateTime.MinValue)));
        Assert.Equal(2, _db.GetAwaitedTitles(_bob).Count);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TestDirectory.Delete(_directory);
        GC.SuppressFinalize(this);
    }
}
