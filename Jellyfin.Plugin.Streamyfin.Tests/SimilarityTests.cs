using Jellyfin.Plugin.Streamyfin.Recommendations;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// How alike two things in the library are.
/// </summary>
/// <remarks>
/// The weights are Jellyfin's own, from the similarity provider Jellyfin 12 ships
/// (<c>MovieSimilarItemsProvider</c>): a shared genre counts 10, a shared tag or studio 5.
/// They are copied rather than invented so that a row here is ordered the way the server
/// would order its own "more like this", on the servers old enough not to have one.
/// </remarks>
public class SimilarityTests
{
    private static ItemFacts Thing(string[]? genres = null, string[]? tags = null, string[]? studios = null) =>
        new(System.Guid.NewGuid(), genres ?? [], tags ?? [], studios ?? []);

    /// <summary>
    /// Two things with nothing in common are not alike.
    /// </summary>
    [Fact]
    public void NothingInCommonIsNotAlike()
    {
        Assert.Equal(0, Similarity.Between(Thing(genres: ["Comedy"]), Thing(genres: ["Horror"])));
        Assert.Equal(0, Similarity.Between(Thing(), Thing()));
    }

    /// <summary>
    /// A genre counts for more than a tag or a studio, and every match adds up.
    /// </summary>
    [Fact]
    public void EachThingSharedAddsItsWeight()
    {
        var seed = Thing(genres: ["Comedy", "Drama"], tags: ["heist"], studios: ["A24"]);

        Assert.Equal(10, Similarity.Between(seed, Thing(genres: ["Comedy"])));
        Assert.Equal(20, Similarity.Between(seed, Thing(genres: ["Comedy", "Drama"])));
        Assert.Equal(5, Similarity.Between(seed, Thing(tags: ["heist"])));
        Assert.Equal(5, Similarity.Between(seed, Thing(studios: ["A24"])));
        Assert.Equal(20, Similarity.Between(seed, Thing(genres: ["Comedy"], tags: ["heist"], studios: ["A24"])));
    }

    /// <summary>
    /// A genre is a genre whatever case the scraper wrote it in, and the padding some
    /// metadata carries is not part of the name.
    /// </summary>
    [Fact]
    public void TheSameNameWrittenDifferentlyStillMatches()
    {
        Assert.Equal(10, Similarity.Between(Thing(genres: ["Science Fiction"]), Thing(genres: ["science fiction"])));
        Assert.Equal(10, Similarity.Between(Thing(genres: ["Comedy"]), Thing(genres: ["  Comedy "])));
    }

    /// <summary>
    /// A genre listed twice on one item is one genre, rather than a way of scoring twice.
    /// </summary>
    [Fact]
    public void ANameListedTwiceCountsOnce()
    {
        Assert.Equal(10, Similarity.Between(Thing(genres: ["Comedy", "Comedy"]), Thing(genres: ["Comedy", "Comedy"])));
    }

    /// <summary>
    /// A blank name is not something two items have in common, however many of them carry
    /// one.
    /// </summary>
    [Fact]
    public void BlankNamesAreNotSomethingInCommon()
    {
        Assert.Equal(0, Similarity.Between(Thing(genres: ["", "  "]), Thing(genres: ["", "  "])));
    }

    /// <summary>
    /// Alikeness does not depend on which of the two is asked about.
    /// </summary>
    [Fact]
    public void ItReadsTheSameBothWays()
    {
        var one = Thing(genres: ["Comedy"], tags: ["heist"]);
        var other = Thing(genres: ["Comedy", "Drama"], studios: ["A24"]);

        Assert.Equal(Similarity.Between(one, other), Similarity.Between(other, one));
    }
}
