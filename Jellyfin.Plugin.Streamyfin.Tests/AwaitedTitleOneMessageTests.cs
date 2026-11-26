using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A person who waited for a title gets one message when it arrives, the awaited title one, and
/// not the new item one too (#289).
/// </summary>
public class AwaitedTitleOneMessageTests
{
    private static User Person(string name) => new(name, "Default", "Default") { Id = Guid.NewGuid() };

    /// <summary>
    /// The new item message leaves out whoever the awaited title message told, and goes to
    /// everyone else it went to before.
    /// </summary>
    [Fact]
    public void TheNewItemMessageLeavesOutWhoTheAwaitedTitleMessageTold()
    {
        var waited = Person("alex");
        var other = Person("dana");

        var goesTo = ItemAddedService.LeaveOut(new HashSet<Guid> { waited.Id }, _ => true);

        Assert.False(goesTo(waited));
        Assert.True(goesTo(other));
    }

    /// <summary>
    /// Who may open the title still decides: leaving people out never adds anybody.
    /// </summary>
    [Fact]
    public void LeavingPeopleOutNeverLetsInWhoCannotOpenTheTitle()
    {
        var goesTo = ItemAddedService.LeaveOut(new HashSet<Guid>(), _ => false);

        Assert.False(goesTo(Person("kid")));
    }

    /// <summary>
    /// A batch of episodes knows when the run its episodes came in began, which says which
    /// awaited title messages were about this arrival.
    /// </summary>
    [Fact]
    public void ABatchKnowsWhenItsArrivalBegan()
    {
        var began = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var batch = new EpisodeTimer([], _ => { }, _ => { }) { Began = began };

        Assert.Equal(began, batch.Began);
    }

    /// <summary>
    /// A new item message counts the awaited title messages sent a little before its arrival
    /// began, since the two notice the title in no set order, and none from an arrival before
    /// it, which ended a minute before this one began.
    /// </summary>
    [Fact]
    public void AMessageCountsTheTellingsOfItsOwnArrival()
    {
        var began = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var since = ItemAddedService.ToldSince(began);

        Assert.True(since < began);
        Assert.True(since > began - TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The wait for a title's id is what the administrator set, ten minutes when they set
    /// nothing, and nothing the server could not count: a value the page refuses, stored by
    /// a build before it did, falls back to the usual wait rather than failing the event.
    /// </summary>
    /// <param name="seconds">What the configuration says.</param>
    /// <param name="expected">The wait, in seconds.</param>
    [Theory]
    [InlineData(null, 600)]
    [InlineData(0d, 0)]
    [InlineData(90d, 90)]
    [InlineData(3600d, 3600)]
    [InlineData(-5d, 600)]
    [InlineData(double.NaN, 600)]
    [InlineData(double.PositiveInfinity, 600)]
    [InlineData(1e12, 3600)]
    public void TheWaitForATitlesIdIsWhatTheAdministratorSet(double? seconds, double expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), ItemAddedService.WaitForTitleId(seconds));
    }

    /// <summary>
    /// The wait for a title's name is what the administrator set, two minutes when they set
    /// nothing or something the server could not count, and ten at most: held longer, a new
    /// item is not news any more.
    /// </summary>
    /// <param name="seconds">What the configuration says.</param>
    /// <param name="expected">The wait, in seconds.</param>
    [Theory]
    [InlineData(null, 120)]
    [InlineData(0d, 0)]
    [InlineData(45d, 45)]
    [InlineData(600d, 600)]
    [InlineData(-5d, 120)]
    [InlineData(double.NaN, 120)]
    [InlineData(double.PositiveInfinity, 120)]
    [InlineData(1e12, 600)]
    public void TheWaitForATitlesNameIsWhatTheAdministratorSet(double? seconds, double expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), ItemAddedService.WaitForTitleName(seconds));
    }

    /// <summary>
    /// A held new item message goes once Jellyfin named its title, when it waits for the name,
    /// and once Jellyfin knows the title's id, when it waits for that too. A stamped refresh ends
    /// both waits: no name and no id come after it.
    /// </summary>
    /// <param name="forName">Whether it waits for the name.</param>
    /// <param name="forId">Whether it waits for the id.</param>
    /// <param name="name">The title's name now; it was added as "Nosferatu (1922) [tmdbid-653]".</param>
    /// <param name="stamped">Whether a refresh stamped it.</param>
    /// <param name="hasId">Whether Jellyfin knows its TMDB id.</param>
    /// <param name="expected">Whether the message goes.</param>
    [Theory]
    [InlineData(true, false, "Nosferatu (1922) [tmdbid-653]", false, true, false)]
    [InlineData(true, false, "Nosferatu le vampire", false, false, true)]
    [InlineData(true, false, "Nosferatu (1922) [tmdbid-653]", true, false, true)]
    [InlineData(false, true, "Nosferatu (1922) [tmdbid-653]", false, true, true)]
    [InlineData(false, true, "Nosferatu le vampire", false, false, false)]
    [InlineData(true, true, "Nosferatu le vampire", false, false, false)]
    [InlineData(true, true, "Nosferatu le vampire", false, true, true)]
    [InlineData(true, true, "Nosferatu (1922) [tmdbid-653]", true, false, true)]
    [InlineData(false, false, "Nosferatu (1922) [tmdbid-653]", false, false, true)]
    public void AHeldMessageGoesOnceWhatItWaitsForCame(bool forName, bool forId, string name, bool stamped, bool hasId, bool expected)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = name };
        if (stamped)
        {
            movie.DateLastRefreshed = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);
        }

        if (hasId)
        {
            movie.ProviderIds["Tmdb"] = "653";
        }

        var ready = ItemAddedService.ReadyWhen("Nosferatu (1922) [tmdbid-653]", forName, forId);

        Assert.Equal(expected, ready(movie));
    }

    /// <summary>
    /// How long a copy of a title leaves out the people told about it is what the administrator
    /// set, an hour when they set nothing, and never past the day the record of who was told is
    /// kept.
    /// </summary>
    /// <param name="seconds">What the configuration says.</param>
    /// <param name="expected">The window, in seconds.</param>
    [Theory]
    [InlineData(null, 3600)]
    [InlineData(0d, 0)]
    [InlineData(5400d, 5400)]
    [InlineData(86400d, 86400)]
    [InlineData(-5d, 3600)]
    [InlineData(double.NaN, 3600)]
    [InlineData(1e12, 86400)]
    public void TheWindowForCopiesIsWhatTheAdministratorSet(double? seconds, double expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), ItemAddedService.CopiesWithin(seconds));
    }

    /// <summary>
    /// The window for copies is counted back from when the arrival began, not from when its
    /// message goes, so a copy held for its id keeps the whole window.
    /// </summary>
    /// <remarks>
    /// Found by the second review of #294: counted from the send, every minute a copy waited
    /// for its id came off the hour.
    /// </remarks>
    [Fact]
    public void TheWindowForCopiesIsCountedFromTheArrival()
    {
        var since = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(since - TimeSpan.FromHours(1), AwaitedTitleAnnouncer.CopiesSince(since, TimeSpan.FromHours(1)));
    }

    /// <summary>
    /// The record of who was told outlives the longest window for copies and the longest wait for
    /// a copy's id together, so a copy that arrives late in the window and waits for its id still
    /// finds the people told.
    /// </summary>
    /// <remarks>Found by the fourth review of #294: both were a day, and the task could forget first.</remarks>
    [Fact]
    public void WhoWasToldIsKeptLongerThanAnyCopyCanTake()
    {
        var longest = TimeSpan.FromSeconds(
            Jellyfin.Plugin.Streamyfin.Configuration.Notifications.ItemAddedNotificationConfiguration.LongestCopiesWithin
            + Jellyfin.Plugin.Streamyfin.Configuration.Notifications.ItemAddedNotificationConfiguration.LongestWaitForTitleId);

        Assert.True(Jellyfin.Plugin.Streamyfin.Configuration.Notifications.AwaitedTitles.ToldKeptFor > longest);
    }

    /// <summary>
    /// A window of nothing leaves nobody out of a copy's message: no telling is ever late enough.
    /// </summary>
    /// <remarks>
    /// Found by the third review of #294: counted back by nothing, the window still took every
    /// telling from the half minute before the copy's own arrival.
    /// </remarks>
    [Fact]
    public void AWindowOfNothingLeavesNobodyOutOfACopysMessage()
    {
        var since = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(DateTime.MaxValue, AwaitedTitleAnnouncer.CopiesSince(since, TimeSpan.Zero));
    }

    /// <summary>
    /// The server stopping sends what is still gathering and what is held, and one batch whose
    /// season cannot be read keeps neither the others nor the held messages from going.
    /// </summary>
    /// <remarks>Found by the third review of #294.</remarks>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OneBatchThatCannotBeReadKeepsNothingFromGoingAsTheServerStops()
    {
        var failures = new List<Exception>();
        var sent = new List<string>();
        var going = new TaskCompletionSource();

        var stopping = ItemAddedService.SendEverything(
            [
                () => throw new InvalidOperationException("the library is closing"),
                () =>
                {
                    sent.Add("batch");
                    return going.Task;
                }
            ],
            () =>
            {
                sent.Add("held");
                return Task.CompletedTask;
            },
            failures.Add);

        Assert.False(stopping.IsCompleted);
        going.SetResult();
        await stopping;

        Assert.Equal(["batch", "held"], sent);
        Assert.Single(failures);
    }
}
