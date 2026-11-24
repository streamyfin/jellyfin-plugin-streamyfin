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
    /// The wait for a title's id is accepted from nothing to an hour, and left empty.
    /// </summary>
    /// <param name="seconds">The wait.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(600d)]
    [InlineData(3600d)]
    public void AWaitForATitlesIdWithinAnHourIsAccepted(double? seconds)
    {
        var notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration { WaitForTitleId = seconds }
        };

        Assert.Null(NotificationsValidation.Check(notifications));
    }

    /// <summary>
    /// The wait for a title's id is refused below nothing, past an hour, and when it is not a
    /// number, named by its key: a server timer cannot count some of those, and a message held
    /// longer than an hour is not news any more.
    /// </summary>
    /// <param name="seconds">The wait.</param>
    [Theory]
    [InlineData(-1d)]
    [InlineData(3601d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AWaitForATitlesIdOutsideAnHourIsRefused(double seconds)
    {
        var notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration { WaitForTitleId = seconds }
        };

        var problem = NotificationsValidation.Check(notifications);

        Assert.NotNull(problem);
        Assert.Contains("itemAdded.waitForTitleId", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// How long a copy of a title leaves out the people told about it is held to its bounds the
    /// same way, read from the property the page reads them from.
    /// </summary>
    /// <param name="seconds">The window.</param>
    /// <param name="refused">Whether it is refused.</param>
    [Theory]
    [InlineData(0d, false)]
    [InlineData(86400d, false)]
    [InlineData(-1d, true)]
    [InlineData(86401d, true)]
    [InlineData(double.NaN, true)]
    public void TheWindowForCopiesIsHeldToItsBounds(double seconds, bool refused)
    {
        var notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration { CopiesWithin = seconds }
        };

        var problem = NotificationsValidation.Check(notifications);

        Assert.Equal(refused, problem is not null);
        if (refused)
        {
            Assert.Contains("itemAdded.copiesWithin", problem!, System.StringComparison.Ordinal);
            Assert.Contains("from 0 to 86400", problem!, System.StringComparison.Ordinal);
        }
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
    /// Every event that waits is checked, not the first one that happens to be declared.
    /// </summary>
    [Fact]
    public void EveryEventThatWaitsIsChecked()
    {
        var notifications = new Notifications
        {
            SignInFailed = new NotificationConfiguration { RecentEventThreshold = -5 }
        };

        Assert.Contains("signInFailed.recentEventThreshold", NotificationsValidation.Check(notifications)!, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A negative wait on an event that never waits is read by nothing, and the Notifications
    /// tab no longer draws it, so one restored from a backup must not refuse every save.
    /// </summary>
    [Fact]
    public void ANegativeWaitOnAnEventThatNeverWaitsIsLeftAlone()
    {
        var notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration { RecentEventThreshold = -5 },
            UserLockedOut = new NotificationConfiguration { RecentEventThreshold = -1 }
        };

        Assert.Null(NotificationsValidation.Check(notifications));
    }

    /// <summary>
    /// A wait that is not a number cannot be stored at all, since the configuration is
    /// written as JSON, so it is refused on every event, in a sentence rather than a 500.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AWaitThatIsNotANumberIsRefusedOnEveryEvent(double seconds)
    {
        var notifications = new Notifications
        {
            ItemAdded = new ItemAddedNotificationConfiguration { RecentEventThreshold = seconds },
            AwaitedTitle = new NotificationConfiguration { RecentEventThreshold = seconds }
        };

        var problem = NotificationsValidation.Check(notifications);

        Assert.Contains("itemAdded.recentEventThreshold", problem!, System.StringComparison.Ordinal);
        Assert.Contains("awaitedTitle.recentEventThreshold", problem!, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A wait longer than the server can count is refused, rather than failing the event
    /// that tries to wait it.
    /// </summary>
    [Fact]
    public void AWaitLongerThanTheServerCanCountIsRefused()
    {
        var notifications = new Notifications
        {
            SignInFailed = new NotificationConfiguration { RecentEventThreshold = 1e12 }
        };

        Assert.Contains("signInFailed.recentEventThreshold", NotificationsValidation.Check(notifications)!, System.StringComparison.Ordinal);
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
