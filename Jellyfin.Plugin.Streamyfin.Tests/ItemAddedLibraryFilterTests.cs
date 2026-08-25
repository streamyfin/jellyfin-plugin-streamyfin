using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
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
}
