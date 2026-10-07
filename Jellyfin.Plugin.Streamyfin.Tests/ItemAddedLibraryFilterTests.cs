using System;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Library filtering rules for the item added notification, covering issue #74.
/// </summary>
public class ItemAddedLibraryFilterTests
{
    /// <summary>
    /// A configuration that never mentions enabledLibraries leaves the property null.
    /// Reading Length on it threw a NullReferenceException inside the event handler,
    /// which is what issue #74 reports.
    /// </summary>
    [Fact]
    public void AbsentLibraryListEnablesEveryLibrary()
    {
        Assert.True(ItemAddedService.IsLibraryEnabled(null, "3a1f0c2e"));
    }

    /// <summary>
    /// An explicitly empty list means the same thing as an absent one.
    /// </summary>
    [Fact]
    public void EmptyLibraryListEnablesEveryLibrary()
    {
        Assert.True(ItemAddedService.IsLibraryEnabled([], "3a1f0c2e"));
    }

    /// <summary>
    /// A library named in the list produces notifications.
    /// </summary>
    [Fact]
    public void ListedLibraryIsEnabled()
    {
        Assert.True(ItemAddedService.IsLibraryEnabled(["3a1f0c2e", "9b7d4e11"], "9b7d4e11"));
    }

    /// <summary>
    /// A library absent from a non empty list does not.
    /// </summary>
    [Fact]
    public void UnlistedLibraryIsDisabled()
    {
        Assert.False(ItemAddedService.IsLibraryEnabled(["3a1f0c2e"], "9b7d4e11"));
    }

    /// <summary>
    /// Comparison is ordinal, so ids differing only by case are different libraries.
    /// </summary>
    [Fact]
    public void LibraryIdComparisonIsOrdinal()
    {
        Assert.False(ItemAddedService.IsLibraryEnabled(["3A1F0C2E"], "3a1f0c2e"));
    }

    /// <summary>
    /// An item whose library could not be identified is not notified about once the
    /// admin has restricted the list, since we cannot tell whether it is allowed.
    /// </summary>
    [Fact]
    public void UnknownLibraryIsDisabledWhenTheListIsRestricted()
    {
        Assert.False(ItemAddedService.IsLibraryEnabled(["3a1f0c2e"], null));
    }

    [Fact]
    public void AnItemBelongsToTheLibraryWhosePathHoldsIt()
    {
        var movies = Guid.NewGuid();
        VirtualFolderInfo[] folders =
        [
            new() { ItemId = movies.ToString("N"), Locations = ["/media/movies"] },
            new() { ItemId = Guid.NewGuid().ToString("N"), Locations = ["/media/shows"] }
        ];

        Assert.Equal(movies, ItemAddedService.LibraryIdOf(folders, "/media/movies/Dune (2021)/Dune.mkv"));
        Assert.Null(ItemAddedService.LibraryIdOf(folders, "/elsewhere/file.mkv"));
        Assert.Null(ItemAddedService.LibraryIdOf(folders, null));
    }

    // A library whose folder's name begins like another's is not taken for that other one.
    [Fact]
    public void ALibraryIsNotTakenForAnotherWhoseNameItStartsWith()
    {
        var movies = Guid.NewGuid();
        var old = Guid.NewGuid();
        VirtualFolderInfo[] folders =
        [
            new() { ItemId = movies.ToString("N"), Locations = ["/media/movies"] },
            new() { ItemId = old.ToString("N"), Locations = ["/media/movies-old"] }
        ];

        Assert.Equal(old, ItemAddedService.LibraryIdOf(folders, "/media/movies-old/Dune (1984)/Dune.mkv"));
        Assert.Equal(movies, ItemAddedService.LibraryIdOf(folders, "/media/movies/Dune (2021)/Dune.mkv"));
    }

    // Inside the folders of two libraries, an item belongs to the nearer one.
    [Fact]
    public void TheNearestLibraryFolderWins()
    {
        var all = Guid.NewGuid();
        var kids = Guid.NewGuid();
        VirtualFolderInfo[] folders =
        [
            new() { ItemId = all.ToString("N"), Locations = ["/media"] },
            new() { ItemId = kids.ToString("N"), Locations = ["/media/kids/"] }
        ];

        Assert.Equal(kids, ItemAddedService.LibraryIdOf(folders, "/media/kids/Bluey/Bluey S01E01.mkv"));
        Assert.Equal(all, ItemAddedService.LibraryIdOf(folders, "/media/films/Up (2009)/Up.mkv"));
    }

    // Jellyfin compares library paths without regard to case, and Windows paths use backslashes.
    [Fact]
    public void AWindowsPathMatchesWhateverItsCase()
    {
        var movies = Guid.NewGuid();
        VirtualFolderInfo[] folders = [new() { ItemId = movies.ToString("N"), Locations = [@"D:\Media\Movies"] }];

        Assert.Equal(movies, ItemAddedService.LibraryIdOf(folders, @"d:\media\movies\Up (2009)\Up.mkv"));
    }

    // Two libraries can hold folders that differ only by case on a filesystem that tells
    // them apart; the folder written exactly as the item's path wins.
    [Fact]
    public void AFolderWrittenExactlyWinsOverOneThatDiffersByCase()
    {
        var upper = Guid.NewGuid();
        var lower = Guid.NewGuid();
        VirtualFolderInfo[] folders =
        [
            new() { ItemId = upper.ToString("N"), Locations = ["/media/Movies"] },
            new() { ItemId = lower.ToString("N"), Locations = ["/media/movies"] }
        ];

        Assert.Equal(lower, ItemAddedService.LibraryIdOf(folders, "/media/movies/Up (2009)/Up.mkv"));
        Assert.Equal(upper, ItemAddedService.LibraryIdOf(folders, "/media/Movies/Up (2009)/Up.mkv"));
    }
}
