using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A new item message held until Jellyfin knows the title's TMDB or TVDB id, for a while at
/// most, so it can leave out the people the awaited title message tells (#289).
/// </summary>
public class IdentifiedTitlesTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private static Movie Movie(string? tmdb = null, Guid? id = null)
    {
        var movie = new Movie { Id = id ?? Guid.NewGuid() };
        if (tmdb is not null)
        {
            movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = tmdb;
        }

        return movie;
    }

    private static Func<BaseItem, Task> Counting(Action<BaseItem> sent) => title =>
    {
        sent(title);
        return Task.CompletedTask;
    };

    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);

    private static Movie MovieNamed(string name, string? tmdb = null, Guid? id = null)
    {
        var movie = Movie(tmdb, id);
        movie.Name = name;
        return movie;
    }

    // Waits for Jellyfin to name the title, whose name was the one given when it was added.
    private static Func<BaseItem, bool> NamedSince(BaseItem added)
    {
        var name = added.Name;
        return title => IdentifiedTitles.Named(title, name);
    }

    /// <summary>
    /// A message held for its title's name goes once an update brings another name than the one
    /// the title was added with, its folder's, and is about the title so named. The save before
    /// the refresh, under the same name, does not let it go.
    /// </summary>
    /// <remarks>
    /// With the TMDB id in its folder name a movie has its id the moment it is added, so only its
    /// name tells that Jellyfin has not looked it up yet.
    /// </remarks>
    [Fact]
    public void AMessageHeldForItsNameGoesOnceAnUpdateRenamesTheTitle()
    {
        BaseItem? about = null;
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var added = MovieNamed("Nosferatu (1922) [tmdbid-653]", "653");
        var saved = MovieNamed(added.Name, "653", added.Id);
        var renamed = MovieNamed("Nosferatu le vampire", "653", added.Id);

        held.Send(added, TwoMinutes, NamedSince(added), Counting(title =>
        {
            sent++;
            about = title;
        }));
        held.Updated(saved);

        Assert.Equal(0, sent);

        held.Updated(renamed);

        Assert.Equal(1, sent);
        Assert.Same(renamed, about);
    }

    /// <summary>
    /// A refresh that left the name as it was, for a title its metadata names like its folder or
    /// one no provider knows, still lets the message go once Jellyfin stamps it.
    /// </summary>
    [Fact]
    public void ARefreshThatKeptTheNameStillLetsTheMessageGo()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var added = MovieNamed("Sintel");
        var refreshed = MovieNamed("Sintel", id: added.Id);
        refreshed.DateLastRefreshed = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);

        held.Send(added, TwoMinutes, NamedSince(added), Counting(_ => sent++));
        held.Updated(refreshed);

        Assert.Equal(1, sent);
    }

    /// <summary>
    /// A message whose title is not named within its wait goes then, about the title as added.
    /// </summary>
    [Fact]
    public void AMessageNotNamedWithinItsWaitGoesThen()
    {
        BaseItem? about = null;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });
        var added = MovieNamed("Nosferatu (1922) [tmdbid-653]", "653");

        held.Send(added, TwoMinutes, NamedSince(added), Counting(title => about = title));
        clock.Advance(TwoMinutes);

        Assert.Same(added, about);
    }

    /// <summary>
    /// Two messages held for one title on different conditions each go when their own is met.
    /// </summary>
    [Fact]
    public void TwoMessagesHeldForOneTitleGoEachOnItsOwnCondition()
    {
        var sent = new List<string>();
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var added = MovieNamed("Nosferatu (1922)");

        held.Send(added, TwoMinutes, NamedSince(added), Counting(_ => sent.Add("name")));
        held.Send(added, TwoMinutes, IdentifiedTitles.HasId, Counting(_ => sent.Add("id")));
        held.Updated(MovieNamed("Nosferatu le vampire", id: added.Id));

        Assert.Equal(new[] { "name" }, sent);

        held.Updated(MovieNamed("Nosferatu le vampire", "653", added.Id));

        Assert.Equal(new[] { "name", "id" }, sent);
    }

    /// <summary>
    /// A batch that read its show before the update that named it goes at once, about the show
    /// as that update left it, as a show identified before its batch does.
    /// </summary>
    [Fact]
    public void AShowNamedBeforeItsBatchIsHeldIsSentAtOnce()
    {
        BaseItem? about = null;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { }, remember: _ => true);
        var copy = MovieNamed("Breaking Bad (2008) [tvdbid-81189]");
        var renamed = MovieNamed("Breaking Bad", id: copy.Id);

        held.Updated(renamed);
        _ = held.Send(copy, TwoMinutes, NamedSince(copy), Counting(title => about = title));

        Assert.Same(renamed, about);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// A title Jellyfin already knows by its id is sent at once.
    /// </summary>
    [Fact]
    public void ATitleWithItsIdIsSentAtOnce()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });

        held.Send(Movie("603"), TenMinutes, Counting(_ => sent++));

        Assert.Equal(1, sent);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// Without a wait, a title without its id is sent at once too: nobody waits for one of
    /// its kind, or the administrator turned the wait off.
    /// </summary>
    [Fact]
    public void WithoutAWaitATitleWithoutItsIdIsSentAtOnce()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });

        held.Send(Movie(), TimeSpan.Zero, Counting(_ => sent++));

        Assert.Equal(1, sent);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// A title without its id waits for the update that brings it, and is sent once.
    /// </summary>
    [Fact]
    public void ATitleWithoutItsIdWaitsForTheUpdateThatBringsIt()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var movie = Movie();

        held.Send(movie, TenMinutes, Counting(_ => sent++));
        held.Updated(movie);
        Assert.Equal(0, sent);

        movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
        held.Updated(movie);
        held.Updated(movie);

        Assert.Equal(1, sent);
    }

    /// <summary>
    /// The message is about the title the update brought, which carries the id, and not the
    /// one that was added.
    /// </summary>
    /// <remarks>
    /// Found by the review of #294. Jellyfin keeps a few hundred items per processor in memory
    /// and reads the others back from its database. A scan adding several shows can push one
    /// out between its last episode and its refresh, so the show a batch was held with is a
    /// copy without the id, while the update brings the scan's own, with it. Settled on the
    /// copy, the arrival found nobody to leave out.
    /// </remarks>
    [Fact]
    public void TheMessageIsAboutTheTitleTheUpdateBrought()
    {
        BaseItem? about = null;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var added = Movie();
        var updated = Movie("603", added.Id);

        held.Send(added, TenMinutes, Counting(title => about = title));
        held.Updated(updated);

        Assert.Same(updated, about);
    }

    /// <summary>
    /// An update of another item sends nothing.
    /// </summary>
    [Fact]
    public void AnUpdateOfAnotherItemSendsNothing()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });

        held.Send(Movie(), TenMinutes, Counting(_ => sent++));
        held.Updated(Movie("603"));

        Assert.Equal(0, sent);
    }

    /// <summary>
    /// Two messages held for the same title both go when its id comes.
    /// </summary>
    [Fact]
    public void TwoMessagesHeldForOneTitleBothGo()
    {
        var sent = new List<string>();
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var movie = Movie();

        held.Send(movie, TenMinutes, Counting(_ => sent.Add("first")));
        held.Send(movie, TenMinutes, Counting(_ => sent.Add("second")));
        movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
        held.Updated(movie);

        Assert.Equal(["first", "second"], sent);
    }

    /// <summary>
    /// An id that comes while the message is being held still sends it at once, whichever
    /// thread gets there first.
    /// </summary>
    /// <remarks>
    /// Found by CodeRabbit on #294. The id was looked at before the lock was taken: an update in
    /// between found nothing held, and the message then waited the whole ten minutes.
    /// </remarks>
    [Fact]
    public void AnIdThatComesWhileTheMessageIsHeldStillSendsIt()
    {
        var late = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });

        Exception? failed = null;

        for (var i = 0; i < 3000; i++)
        {
            var movie = Movie();
            var sent = 0;
            using var start = new Barrier(2);
            // Caught here and looked at after the join: thrown on a thread of its own, it ended
            // the whole test run rather than this test.
            void Run(Action action)
            {
                try
                {
                    start.SignalAndWait();
                    action();
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref failed, e, null);
                }
            }

            var sending = new Thread(() => Run(() => held.Send(movie, TenMinutes, _ =>
            {
                Interlocked.Increment(ref sent);
                return Task.CompletedTask;
            })));
            var updating = new Thread(() => Run(() =>
            {
                movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
                held.Updated(movie);
            }));

            sending.Start();
            updating.Start();
            sending.Join();
            updating.Join();

            if (Volatile.Read(ref sent) != 1)
            {
                late++;
            }
        }

        Assert.Null(failed);
        Assert.Equal(0, late);
    }

    /// <summary>
    /// A title's ids are read while Jellyfin writes them, on the timer's thread and the
    /// refresh's: the read waits for the write rather than failing.
    /// </summary>
    /// <remarks>
    /// Found by the second review of #294, through the race test above, which ended the test
    /// run about once in a hundred: Jellyfin keeps an item's ids in a plain dictionary and
    /// writes them in place during a refresh, so a read that meets the write throws "Collection
    /// was modified". On a server, that lost the batch of episodes being read.
    /// </remarks>
    [Fact]
    public void ATitlesIdsAreReadWhileJellyfinWritesThem()
    {
        var movie = Movie();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Exception? failed = null;

        var writing = new Thread(() =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
            {
                movie.ProviderIds[$"Other{i % 64}"] = "1";
                movie.ProviderIds.Remove($"Other{(i + 32) % 64}");
            }
        });

        writing.Start();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                IdentifiedTitles.HasId(movie);
            }
        }
        catch (Exception e)
        {
            failed = e;
        }

        writing.Join();
        Assert.Null(failed);
    }

    /// <summary>
    /// A title whose metadata refresh ended without an id is sent then, about the title as the
    /// refresh left it: no id is coming, and holding it longer only makes it late.
    /// </summary>
    /// <remarks>
    /// Found by the second review of #294. Only an update that brought the id let a message go,
    /// so a show known by an AniDB id only, or a home video, waited the whole wait whenever
    /// anybody waited for a title of its kind.
    /// </remarks>
    [Fact]
    public void ATitleWhoseRefreshEndedWithoutAnIdIsSentThen()
    {
        BaseItem? about = null;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var added = Movie();
        var refreshed = Movie(id: added.Id);
        refreshed.DateLastRefreshed = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

        held.Send(added, TenMinutes, Counting(title => about = title));
        held.Updated(refreshed);

        Assert.Same(refreshed, about);
    }

    /// <summary>
    /// A title whose metadata already came without an id, a show known by its AniDB id only
    /// getting new episodes, is sent at once: no id is coming.
    /// </summary>
    [Fact]
    public void ATitleAlreadyRefreshedWithoutAnIdIsSentAtOnce()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });
        var show = Movie();
        show.DateLastRefreshed = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        held.Send(show, TenMinutes, Counting(_ => sent++));

        Assert.Equal(1, sent);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// Jellyfin saves a new item once before its first refresh asks for the ids: that update
    /// does not end the wait.
    /// </summary>
    [Fact]
    public void TheSaveBeforeTheFirstRefreshDoesNotEndTheWait()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { });
        var movie = Movie();

        held.Send(movie, TenMinutes, Counting(_ => sent++));
        held.Updated(Movie(id: movie.Id));

        Assert.Equal(0, sent);
    }

    /// <summary>
    /// A title that never gets an id is sent once its wait is over, and not a moment before.
    /// </summary>
    [Fact]
    public void ATitleThatNeverGetsAnIdIsSentWhenItsWaitIsOver()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });
        var movie = Movie();

        held.Send(movie, TenMinutes, Counting(_ => sent++));
        clock.Advance(TenMinutes - TimeSpan.FromSeconds(1));
        Assert.Equal(0, sent);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, sent);
    }

    /// <summary>
    /// Sent when its wait is over, a message is about the title as it was added: nothing
    /// newer came.
    /// </summary>
    [Fact]
    public void AMessageWhoseWaitIsOverIsAboutTheTitleAdded()
    {
        BaseItem? about = null;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });
        var movie = Movie();

        held.Send(movie, TenMinutes, Counting(title => about = title));
        clock.Advance(TenMinutes);

        Assert.Same(movie, about);
    }

    /// <summary>
    /// Every held message goes off one timer, in the order their waits end, so titles added
    /// together are sent one after the other rather than all at once.
    /// </summary>
    /// <remarks>
    /// Found by the review of #294. A timer per title fired two thousand callbacks on the
    /// thread pool the server answers requests with, when a scan added two thousand movies
    /// without an id in their folder names.
    /// </remarks>
    [Fact]
    public void HeldMessagesGoOffOneTimerInTheOrderTheirWaitsEnd()
    {
        var sent = new List<string>();
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });

        held.Send(Movie(), TenMinutes, Counting(_ => sent.Add("ten minutes")));
        held.Send(Movie(), TimeSpan.FromMinutes(2), Counting(_ => sent.Add("two minutes")));
        held.Send(Movie(), TimeSpan.FromMinutes(5), Counting(_ => sent.Add("five minutes")));

        Assert.Equal(1, clock.Armed);
        clock.Advance(TenMinutes);

        Assert.Equal(["two minutes", "five minutes", "ten minutes"], sent);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// A title sent when its id came is not sent again when its wait ends.
    /// </summary>
    [Fact]
    public void ATitleSentWhenItsIdCameIsNotSentAgainLater()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });
        var movie = Movie();

        held.Send(movie, TenMinutes, Counting(_ => sent++));
        movie.ProviderIds[nameof(MetadataProvider.Tmdb)] = "603";
        held.Updated(movie);
        clock.Advance(TenMinutes * 2);

        Assert.Equal(1, sent);
    }

    /// <summary>
    /// A message that fails is reported, and the next one still goes.
    /// </summary>
    [Fact]
    public void AMessageThatFailsIsReportedAndStopsNothing()
    {
        var failures = new List<Exception>();
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), failures.Add);

        held.Send(Movie("603"), TenMinutes, _ => throw new InvalidOperationException("the movie is gone"));
        held.Send(Movie("604"), TenMinutes, _ => Task.FromException(new InvalidOperationException("Expo is down")));
        held.Send(Movie("605"), TenMinutes, Counting(_ => sent++));

        Assert.Equal(2, failures.Count);
        Assert.Equal(1, sent);
    }

    /// <summary>
    /// A show whose update brought its id before its batch was held is sent at once, about the
    /// show the update brought: the batch may read a copy Jellyfin kept from before the update,
    /// and no update is coming to release it.
    /// </summary>
    /// <remarks>
    /// Found by the fourth review of #294. Jellyfin's cache keeps only so many items and does not
    /// take back the one a refresh saved, so the copy a batch read could lack the ids its update
    /// already brought; read back from the library instead, the update could still land between
    /// the read and the hold. Either way the batch waited the whole wait and left nobody out.
    /// </remarks>
    [Fact]
    public void AShowIdentifiedBeforeItsBatchIsHeldIsSentAtOnce()
    {
        BaseItem? about = null;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { }, remember: _ => true);
        var updated = Movie("603");
        var copy = Movie(id: updated.Id);

        held.Updated(updated);
        _ = held.Send(copy, TenMinutes, Counting(title => about = title));

        Assert.Same(updated, about);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// Only the titles it is told to remember are remembered: a scan updates every item it
    /// touches, and keeping them all would keep the whole library in memory.
    /// </summary>
    [Fact]
    public void OnlyTheTitlesToRememberAreRemembered()
    {
        var sent = 0;
        using var held = new IdentifiedTitles(new TestClock(), _ => { }, remember: _ => false);
        var updated = Movie("603");

        held.Updated(updated);
        _ = held.Send(Movie(id: updated.Id), TenMinutes, Counting(_ => sent++));

        Assert.Equal(0, sent);
    }

    /// <summary>
    /// What is remembered is forgotten after a while, so a title updated long before is held
    /// again like any other.
    /// </summary>
    [Fact]
    public void WhatIsRememberedIsForgottenAfterAWhile()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { }, remember: _ => true);
        var updated = Movie("603");

        held.Updated(updated);
        clock.Advance(TimeSpan.FromMinutes(11));
        _ = held.Send(Movie(id: updated.Id), TenMinutes, Counting(_ => sent++));

        Assert.Equal(0, sent);
    }

    /// <summary>
    /// Once the server stopped, nothing is held any more: a batch closing as it stops, or an
    /// event still running, would wait for a timer the stop leaves behind.
    /// </summary>
    /// <remarks>Found by the fourth review of #294.</remarks>
    /// <returns>A task.</returns>
    [Fact]
    public async Task NothingIsHeldOnceTheServerStopped()
    {
        var sent = 0;
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });

        await held.SendAll();
        _ = held.Send(Movie(), TenMinutes, Counting(_ => sent++));

        Assert.Equal(1, sent);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// A send that ends canceled, Expo slower than the client's timeout, is reported like one that
    /// failed: lost either way.
    /// </summary>
    /// <remarks>Found by the third review of #294: only a send that failed was heard of.</remarks>
    [Fact]
    public void ASendThatEndsCanceledIsReported()
    {
        var failures = new List<Exception>();
        using var held = new IdentifiedTitles(new TestClock(), failures.Add);

        _ = held.Send(Movie("603"), TenMinutes, _ => Task.FromCanceled(new CancellationToken(canceled: true)));

        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(failures));
    }

    /// <summary>
    /// When the server stops, everything held is sent at once, about the title as it was
    /// added, and nothing is sent for it afterwards.
    /// </summary>
    /// <remarks>
    /// Found by the review of #294. What is held lives in memory: dropped as the server stopped,
    /// a movie added within the wait before a restart was announced to nobody, since Jellyfin
    /// does not add it again.
    /// </remarks>
    [Fact]
    public async Task StoppingSendsWhatWasHeld()
    {
        var sent = new List<BaseItem>();
        var clock = new TestClock();
        using var held = new IdentifiedTitles(clock, _ => { });
        var first = Movie();
        var second = Movie();

        _ = held.Send(first, TenMinutes, Counting(sent.Add));
        _ = held.Send(second, TenMinutes, Counting(sent.Add));
        await held.SendAll();
        clock.Advance(TenMinutes * 2);
        held.Updated(Movie("603", first.Id));

        Assert.Equal([first, second], sent);
        Assert.Equal(0, clock.Armed);
    }

    /// <summary>
    /// Stopping waits for the messages it sends, so the server does not go before they do,
    /// and a send that fails does not keep the server from stopping.
    /// </summary>
    [Fact]
    public async Task StoppingWaitsForWhatItSends()
    {
        var failures = new List<Exception>();
        var going = new TaskCompletionSource();
        using var held = new IdentifiedTitles(new TestClock(), failures.Add);

        _ = held.Send(Movie(), TenMinutes, _ => going.Task);
        _ = held.Send(Movie(), TenMinutes, _ => Task.FromException(new InvalidOperationException("Expo is down")));
        var stopping = held.SendAll();

        Assert.False(stopping.IsCompleted);
        going.SetResult();
        await stopping;
        Assert.Single(failures);
    }

    /// <summary>
    /// Disposed, nothing held is sent any more.
    /// </summary>
    [Fact]
    public void DisposedNothingIsSent()
    {
        var sent = 0;
        var clock = new TestClock();
        var held = new IdentifiedTitles(clock, _ => { });

        held.Send(Movie(), TenMinutes, Counting(_ => sent++));
        held.Dispose();
        clock.Advance(TenMinutes * 2);

        Assert.Equal(0, sent);
    }
}
