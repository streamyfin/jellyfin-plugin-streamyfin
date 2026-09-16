using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Where a home section sits, which used to be the order it happened to be written in.
/// </summary>
public class SectionOrderTests
{
    /// <summary>
    /// Nothing declares an order, so nothing moves. This is every configuration that
    /// exists today.
    /// </summary>
    [Fact]
    public void SectionsWithoutAnOrderKeepTheOrderTheyAreWrittenIn()
    {
        var home = HomeOf(("first", null), ("second", null), ("third", null));

        Sections.Sort(home);

        Assert.Equal(new[] { "first", "second", "third" }, Titles(home));
    }

    /// <summary>
    /// A declared order decides, lowest first.
    /// </summary>
    [Fact]
    public void ADeclaredOrderDecides()
    {
        var home = HomeOf(("last", 30), ("first", 10), ("middle", 20));

        Sections.Sort(home);

        Assert.Equal(new[] { "first", "middle", "last" }, Titles(home));
    }

    /// <summary>
    /// A section that declares nothing keeps its place, and one that does is placed by
    /// it, so a configuration can be numbered a section at a time rather than all at
    /// once.
    /// </summary>
    [Fact]
    public void ASectionWithoutAnOrderKeepsItsPlaceAmongOnesThatHaveOne()
    {
        var home = HomeOf(("written first", null), ("pinned to the top", 0), ("written third", null));

        Sections.Sort(home);

        Assert.Equal(new[] { "pinned to the top", "written first", "written third" }, Titles(home));
    }

    /// <summary>
    /// Two sections claiming the same number keep the order they were written in, every
    /// time. An administrator who numbers two the same has said they do not mind, not
    /// that the server may answer differently on each request.
    /// </summary>
    [Fact]
    public void TheSameNumberTwiceKeepsTheWrittenOrder()
    {
        var home = HomeOf(("a", 5), ("b", 5), ("c", 5));

        Sections.Sort(home);
        Sections.Sort(home);

        Assert.Equal(new[] { "a", "b", "c" }, Titles(home));
    }

    /// <summary>
    /// A negative number is a way to put something above everything else without
    /// renumbering what is already there.
    /// </summary>
    [Fact]
    public void ANegativeOrderGoesAboveTheRest()
    {
        var home = HomeOf(("ordinary", null), ("above everything", -1));

        Sections.Sort(home);

        Assert.Equal(new[] { "above everything", "ordinary" }, Titles(home));
    }

    /// <summary>
    /// Nothing to sort is not a failure.
    /// </summary>
    [Fact]
    public void NothingToSortIsNotAFailure()
    {
        Sections.Sort((Home?)null);
        Sections.Sort(new Home());
        Sections.Sort(new Home { sections = [] });
        Sections.Sort((Settings?)null);
        Sections.Sort(new Settings());
    }

    /// <summary>
    /// The settings carry the home layout in a lockable, and sorting reaches through it.
    /// </summary>
    [Fact]
    public void SortingReachesTheHomeInsideTheSettings()
    {
        var settings = new Settings
        {
            home = new Lockable<Home> { value = HomeOf(("second", 20), ("first", 10)) }
        };

        Sections.Sort(settings);

        Assert.Equal(new[] { "first", "second" }, Titles(settings.home.value));
    }

    /// <summary>
    /// Two routes both sort the same objects, so sorting twice has to give the same
    /// answer. It did not: the second pass read the place the first pass had moved a
    /// section to as if the administrator had asked for it.
    /// </summary>
    [Fact]
    public void SortingTwiceGivesTheSameAnswer()
    {
        var home = HomeOf(("written first", null), ("pinned to the top", 0), ("written third", 1));

        Sections.Sort(home);
        var once = Titles(home);
        Sections.Sort(home);

        Assert.Equal(once, Titles(home));
        Assert.Equal(new[] { "pinned to the top", "written first", "written third" }, Titles(home));
    }

    /// <summary>
    /// A section says where it ended up, which is what makes a second pass a no-op.
    /// </summary>
    [Fact]
    public void EverySectionLeavesSayingWhereItSits()
    {
        var home = HomeOf(("second", null), ("first", -3));

        Sections.Sort(home);

        Assert.Equal(new int?[] { 0, 1 }, (home.sections ?? []).Select(section => section.order).ToArray());
    }

    private static Home HomeOf(params (string Title, int? Order)[] sections) => new()
    {
        sections = sections
            .Select(section => new Section
            {
                title = section.Title,
                order = section.Order,
                latest = new Latest()
            })
            .ToArray()
    };

    private static string[] Titles(Home? home) =>
        (home?.sections ?? []).Select(section => section.title).ToArray();
}
