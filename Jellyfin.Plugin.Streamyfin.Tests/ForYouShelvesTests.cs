using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Recommendations;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Keeping a shelf around for a little while.
/// </summary>
/// <remarks>
/// Building a shelf reads everything unplayed in the genres somebody watches and scores it.
/// The app then asks for it a page at a time while somebody scrolls, and rebuilding it for
/// every page would both cost the server and shuffle the shelf under the scroll.
/// </remarks>
public class ForYouShelvesTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// A second page of the same shelf is the same shelf, built once.
    /// </summary>
    [Fact]
    public void TheSameShelfIsBuiltOnce()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);
        var built = 0;

        List<Guid> Build()
        {
            built++;
            return [Guid.NewGuid()];
        }

        var first = shelves.For(Alice, Build);
        var second = shelves.For(Alice, Build);

        Assert.Equal(1, built);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// One person's shelf is not another's.
    /// </summary>
    [Fact]
    public void EverybodyHasTheirOwn()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);

        var hers = shelves.For(Alice, () => [Alice]);
        var his = shelves.For(Bob, () => [Bob]);

        Assert.Equal([Alice], hers);
        Assert.Equal([Bob], his);
    }

    /// <summary>
    /// It goes stale, so that something watched this afternoon changes what is recommended
    /// this evening.
    /// </summary>
    [Fact]
    public void ItGoesStale()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);
        var built = 0;

        List<Guid> Build()
        {
            built++;
            return [Guid.NewGuid()];
        }

        shelves.For(Alice, Build);
        now += ForYouShelves.KeptFor + TimeSpan.FromSeconds(1);
        shelves.For(Alice, Build);

        Assert.Equal(2, built);
    }

    /// <summary>
    /// Something that was just watched, or added, is worth a fresh shelf before it goes
    /// stale by itself.
    /// </summary>
    [Fact]
    public void AShelfCanBeThrownAway()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);
        var built = 0;

        List<Guid> Build()
        {
            built++;
            return [Guid.NewGuid()];
        }

        shelves.For(Alice, Build);
        shelves.Forget(Alice);
        shelves.For(Alice, Build);

        Assert.Equal(2, built);
    }

    /// <summary>
    /// Two requests landing together build one shelf rather than two, which is what
    /// happens when the app asks for a row and its first page at once.
    /// </summary>
    [Fact]
    public async Task TwoRequestsAtOnceBuildOneShelf()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);
        var built = 0;

        List<Guid> Build()
        {
            System.Threading.Interlocked.Increment(ref built);
            System.Threading.Thread.Sleep(20);
            return [Guid.NewGuid()];
        }

        var answers = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => shelves.For(Alice, Build))));

        Assert.Equal(1, built);
        Assert.All(answers, answer => Assert.Equal(answers[0], answer));
    }

    /// <summary>
    /// A shelf asked for differently is a different shelf: the same person can be handed
    /// one built from three things watched and one built from thirty, and neither should
    /// be served in place of the other.
    /// </summary>
    [Fact]
    public void AskedForDifferentlyIsADifferentShelf()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);

        var few = shelves.For(Alice, () => [Alice], "seeds=3");
        var many = shelves.For(Alice, () => [Bob], "seeds=30");

        Assert.Equal([Alice], few);
        Assert.Equal([Bob], many);
    }

    /// <summary>
    /// Throwing somebody's shelf away throws away every way they asked for it, since what
    /// changed underneath changed for all of them.
    /// </summary>
    [Fact]
    public void ThrowingOneAwayThrowsAwayEveryWayItWasAskedFor()
    {
        var now = DateTime.UtcNow;
        var shelves = new ForYouShelves(() => now);
        var built = 0;

        List<Guid> Build()
        {
            built++;
            return [Guid.NewGuid()];
        }

        shelves.For(Alice, Build, "seeds=3");
        shelves.For(Alice, Build, "seeds=30");
        shelves.Forget(Alice);
        shelves.For(Alice, Build, "seeds=3");
        shelves.For(Alice, Build, "seeds=30");

        Assert.Equal(4, built);
    }
}
