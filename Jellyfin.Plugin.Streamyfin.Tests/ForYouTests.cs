using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.Recommendations;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The order a "for you" row puts things in.
/// </summary>
/// <remarks>
/// The idea is the one the request describes: take what somebody has watched, ask Jellyfin
/// what each of those is like, and put forward what keeps coming up. Jellyfin answers each
/// question in its own order, best first, so where something sits in an answer counts as
/// well as how often it appears.
/// </remarks>
public class ForYouTests
{
    private static readonly Guid Seen = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Once = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Twice = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Third = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// What is like two things somebody watched comes before what is like one.
    /// </summary>
    [Fact]
    public void WhatKeepsComingUpComesFirst()
    {
        var ranked = ForYou.Ranked([[Once, Twice], [Twice, Third]], []);

        Assert.Equal(Twice, ranked[0]);
    }

    /// <summary>
    /// Where something sits in an answer counts: Jellyfin puts what it thinks is closest
    /// first, and a row should not throw that away.
    /// </summary>
    [Fact]
    public void WhereItSitsInAnAnswerCounts()
    {
        var ranked = ForYou.Ranked([[Once, Twice, Third]], []);

        Assert.Equal([Once, Twice, Third], ranked);
    }

    /// <summary>
    /// What they have already watched is not a recommendation, and neither is the thing the
    /// question was asked about.
    /// </summary>
    [Fact]
    public void WhatTheyHaveSeenIsNotSuggested()
    {
        var ranked = ForYou.Ranked([[Seen, Once], [Seen, Twice]], new HashSet<Guid> { Seen });

        Assert.DoesNotContain(Seen, ranked);
        Assert.Equal(2, ranked.Count);
    }

    /// <summary>
    /// One answer naming the same thing twice is one answer about it.
    /// </summary>
    [Fact]
    public void ThingsNamedTwiceInOneAnswerCountOnce()
    {
        var ranked = ForYou.Ranked([[Once, Once, Once], [Twice, Third]], []);

        Assert.Equal([Once, Twice, Third], ranked);
    }

    /// <summary>
    /// Nothing watched, or nothing like it, is an empty row rather than an error.
    /// </summary>
    [Fact]
    public void NothingToGoOnIsAnEmptyRow()
    {
        Assert.Empty(ForYou.Ranked([], []));
        Assert.Empty(ForYou.Ranked([[]], []));
        Assert.Empty(ForYou.Ranked([[Seen]], new HashSet<Guid> { Seen }));
    }

    /// <summary>
    /// Two things that come up as often keep the order Jellyfin answered in, so the row is
    /// the same on every load rather than shuffling.
    /// </summary>
    [Fact]
    public void ATieKeepsTheOrderItCameIn()
    {
        var first = ForYou.Ranked([[Once], [Twice]], []);
        var again = ForYou.Ranked([[Once], [Twice]], []);

        Assert.Equal([Once, Twice], first);
        Assert.Equal(first, again);
    }

    private static ItemFacts Thing(string name, params string[] genres) =>
        new(Guid.Parse(name), genres, [], []);

    /// <summary>
    /// The shelf itself: what somebody watched on one side, what they have not on the
    /// other, and an order out of it.
    /// </summary>
    [Fact]
    public void TheShelfPutsWhatMatchesMostWatchedFirst()
    {
        var seeds = new[]
        {
            Thing("11111111-1111-1111-1111-111111111111", "Comedy"),
            Thing("11111111-1111-1111-1111-111111111112", "Drama")
        };

        var candidates = new[]
        {
            Thing("22222222-2222-2222-2222-222222222221", "Comedy"),
            Thing("22222222-2222-2222-2222-222222222222", "Comedy", "Drama"),
            Thing("22222222-2222-2222-2222-222222222223", "Horror")
        };

        var shelf = ForYou.Shelf(seeds, candidates);

        Assert.Equal(
            [Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("22222222-2222-2222-2222-222222222221")],
            shelf);
    }

    /// <summary>
    /// What was watched is never put back on the shelf, even when it is handed in on both
    /// sides, which is what happens when the candidates come from a query that did not
    /// exclude them.
    /// </summary>
    [Fact]
    public void WhatWasWatchedNeverComesBack()
    {
        var seed = Thing("11111111-1111-1111-1111-111111111111", "Comedy");
        var other = Thing("22222222-2222-2222-2222-222222222221", "Comedy");

        Assert.Equal([other.Id], ForYou.Shelf([seed], [seed, other]));
    }

    /// <summary>
    /// Only so much of each answer counts. Without a cap, one seed in a huge genre fills
    /// the shelf with everything that shares that genre, and the shelf stops being about
    /// what somebody watched.
    /// </summary>
    [Fact]
    public void OnlySoMuchOfEachAnswerCounts()
    {
        var seeds = new[] { Thing("11111111-1111-1111-1111-111111111111", "Comedy") };

        var candidates = new[]
        {
            Thing("22222222-2222-2222-2222-222222222221", "Comedy"),
            Thing("22222222-2222-2222-2222-222222222222", "Comedy"),
            Thing("22222222-2222-2222-2222-222222222223", "Comedy")
        };

        Assert.Equal(2, ForYou.Shelf(seeds, candidates, perSeed: 2).Count);
    }
}
