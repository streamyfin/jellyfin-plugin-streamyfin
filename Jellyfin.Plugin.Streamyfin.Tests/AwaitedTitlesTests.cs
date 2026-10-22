using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The rules of the titles people wait for (#225).
/// </summary>
public class AwaitedTitlesTests
{
    /// <summary>
    /// What an arrival comes to for one person: told now, told after their pause, still
    /// waiting for a copy they can open, or not waiting any more.
    /// </summary>
    [Theory]
    [InlineData(true, true, true, false, AwaitedOutcome.Announce)]
    [InlineData(true, true, true, true, AwaitedOutcome.Hold)]
    [InlineData(true, true, false, false, AwaitedOutcome.KeepWaiting)]
    [InlineData(true, true, false, true, AwaitedOutcome.KeepWaiting)]
    [InlineData(true, false, true, false, AwaitedOutcome.Drop)]
    [InlineData(false, true, true, false, AwaitedOutcome.Drop)]
    public void WhatAnArrivalComesTo(bool exists, bool reaches, bool canOpen, bool paused, AwaitedOutcome expected) =>
        Assert.Equal(expected, AwaitedTitles.Decide(exists, reaches, canOpen, paused));

    /// <summary>
    /// A title is checked before it is kept, and a refusal says what is wrong.
    /// </summary>
    [Theory]
    [InlineData("movie", 603, null, "The Matrix", null)]
    [InlineData("tv", 1399, 121361, "Game of Thrones", null)]
    [InlineData("music", 1, null, "Album", "movie or a tv show")]
    [InlineData(null, 1, null, "Nothing", "movie or a tv show")]
    [InlineData("movie", 0, null, "No id", "TMDB id")]
    [InlineData("tv", 1, 0, "Bad TVDB", "TVDB id")]
    [InlineData("movie", 1, null, " ", "name")]
    public void ATitleIsCheckedBeforeItIsKept(string? mediaType, int tmdbId, int? tvdbId, string? title, string? refusal)
    {
        var problem = AwaitedTitles.Problem(mediaType, tmdbId, tvdbId, title);

        if (refusal is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.NotNull(problem);
            Assert.Contains(refusal, problem, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A name longer than any title is refused.
    /// </summary>
    [Fact]
    public void ATitleTooLongIsRefused() =>
        Assert.NotNull(AwaitedTitles.Problem("movie", 1, null, new string('a', AwaitedTitles.LongestTitle + 1)));

    /// <summary>
    /// Jellyfin's provider ids are read whatever their case.
    /// </summary>
    [Fact]
    public void TheIdsAreReadWhateverTheirCase() =>
        Assert.Equal<(int?, int?)>(
            (603, 290434),
            AwaitedTitles.IdsOf(new Dictionary<string, string> { ["tmdb"] = "603", ["Tvdb"] = "290434" }));

    /// <summary>
    /// An id that is not a positive number is no id at all.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("")]
    public void AnIdThatIsNotAPositiveNumberIsNoId(string raw) =>
        Assert.Equal<(int?, int?)>((null, null), AwaitedTitles.IdsOf(new Dictionary<string, string> { ["Tmdb"] = raw }));

    /// <summary>
    /// An item without provider ids has none to match.
    /// </summary>
    [Fact]
    public void AnItemWithoutProviderIdsHasNone() =>
        Assert.Equal<(int?, int?)>((null, null), AwaitedTitles.IdsOf(null));
}
