using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What the server refuses to store about an event.
/// </summary>
/// <remarks>
/// The admin page puts a floor of zero on the wait between two of the same event, and
/// until this existed the page was the only thing that knew: the Yaml tab writes what is
/// typed, so a wait of minus one reached the database.
/// </remarks>
public class NotificationsValidationTests
{
    /// <summary>
    /// Nothing to check is not a refusal.
    /// </summary>
    [Fact]
    public void NothingToCheckIsAccepted()
    {
        Assert.Null(NotificationsValidation.Check(null));
        Assert.Null(NotificationsValidation.Check(new Notifications()));
    }

    /// <summary>
    /// A wait that is one is accepted, including none at all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(30d)]
    public void AWaitThatIsOneIsAccepted(double? seconds)
    {
        var notifications = new Notifications
        {
            SessionStarted = new NotificationConfiguration { Enabled = true, RecentEventThreshold = seconds }
        };

        Assert.Null(NotificationsValidation.Check(notifications));
    }

    /// <summary>
    /// A negative wait is refused, and the message names the key so an administrator can
    /// find it in the file they wrote it in.
    /// </summary>
    [Fact]
    public void ANegativeWaitIsRefusedAndNamed()
    {
        var notifications = new Notifications
        {
            SessionStarted = new NotificationConfiguration { RecentEventThreshold = -1 }
        };

        var problem = NotificationsValidation.Check(notifications);

        Assert.NotNull(problem);
        Assert.Contains("sessionStarted.recentEventThreshold", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Every event is checked, not the first one that happens to be declared.
    /// </summary>
    [Fact]
    public void EveryEventIsChecked()
    {
        var notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration { RecentEventThreshold = -5 }
        };

        Assert.Contains("itemAdded.recentEventThreshold", NotificationsValidation.Check(notifications)!, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A number that is not one cannot be a wait either.
    /// </summary>
    [Fact]
    public void ANumberThatIsNotOneIsRefused()
    {
        var notifications = new Notifications
        {
            PlaybackStarted = new NotificationConfiguration { RecentEventThreshold = double.NaN }
        };

        Assert.NotNull(NotificationsValidation.Check(notifications));
    }
}
