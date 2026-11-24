using System;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events.ItemAdded;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// When a show's run of new episodes began: the episodes of a show that come less than a
/// batch's minute apart arrive together, whichever season they are in (#289).
/// </summary>
public class ShowRunsTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Episodes that keep coming less than a minute apart are one run, so a season reached
    /// two minutes into the scan still belongs to the arrival its first episode began.
    /// </summary>
    /// <remarks>
    /// Found while fixing the review of #294. A show that arrives with several seasons is a
    /// batch per season, each opened as the scan reaches it, and a batch only counted the
    /// awaited title messages sent half a minute before it opened: past that, the person
    /// told the show had arrived got the later seasons' batches too.
    /// </remarks>
    [Fact]
    public void EpisodesLessThanAMinuteApartAreOneRun()
    {
        var runs = new ShowRuns(Minute);
        var show = Guid.NewGuid();

        Assert.Equal(Start, runs.Add(show, Start));
        Assert.Equal(Start, runs.Add(show, Start.AddSeconds(50)));
        Assert.Equal(Start, runs.Add(show, Start.AddSeconds(100)));
        Assert.Equal(Start, runs.Add(show, Start.AddSeconds(150)));
    }

    /// <summary>
    /// An episode more than a minute after the last one begins a run of its own: more of the
    /// show, which reaches everyone like any new episode.
    /// </summary>
    [Fact]
    public void AnEpisodeMoreThanAMinuteLaterBeginsARun()
    {
        var runs = new ShowRuns(Minute);
        var show = Guid.NewGuid();
        runs.Add(show, Start);

        var later = Start.AddSeconds(61);

        Assert.Equal(later, runs.Add(show, later));
    }

    /// <summary>
    /// Each show has its own run.
    /// </summary>
    [Fact]
    public void EachShowHasItsOwnRun()
    {
        var runs = new ShowRuns(Minute);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        runs.Add(first, Start);

        Assert.Equal(Start.AddSeconds(30), runs.Add(second, Start.AddSeconds(30)));
        Assert.Equal(Start, runs.Add(first, Start.AddSeconds(40)));
    }

    /// <summary>
    /// A run that ended is forgotten, and one still going is kept.
    /// </summary>
    [Fact]
    public void ARunThatEndedIsForgotten()
    {
        var runs = new ShowRuns(Minute);
        var ended = Guid.NewGuid();
        var going = Guid.NewGuid();
        runs.Add(ended, Start);
        runs.Add(going, Start.AddSeconds(90));

        runs.Forget(Start.AddSeconds(100));

        Assert.Equal(1, runs.Count);
        Assert.Equal(Start.AddSeconds(90), runs.Add(going, Start.AddSeconds(110)));
    }
}
