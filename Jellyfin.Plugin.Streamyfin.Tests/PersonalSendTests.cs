using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The person's own rule, as the targeted send asks it.
/// </summary>
public class PersonalSendTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Show = Guid.NewGuid();

    private sealed class Watching(bool favorite, bool started) : IShowWatching
    {
        public int StartedAsked { get; private set; }

        public bool IsFavorite(User user, Guid seriesId) => favorite;

        public bool HasStarted(User user, Guid seriesId)
        {
            StartedAsked++;
            return started;
        }
    }

    private static User Alice => new("alice", "provider", "reset");

    [Fact]
    public void AFavoriteShowReachesSomebodyWhoTurnedNewItemsOff()
    {
        var mine = new NotificationPreferences();
        mine.Events[NotificationEvents.ItemAdded] = false;

        Assert.True(NotificationHelper.KeepsFor(mine, new NotificationSubject(NotificationEvents.ItemAdded, SeriesId: Show), Now, Alice, new Watching(favorite: true, started: false)));
    }

    [Fact]
    public void WithoutAWayToAskAShowIsNotFollowed()
    {
        var mine = new NotificationPreferences();
        mine.Events[NotificationEvents.ItemAdded] = false;

        Assert.False(NotificationHelper.KeepsFor(mine, new NotificationSubject(NotificationEvents.ItemAdded, SeriesId: Show), Now, Alice, watching: null));
    }

    // Somebody who chose nothing costs no library query.
    [Fact]
    public void SomebodyWhoChoseNothingIsNeverAskedAbout()
    {
        var watching = new Watching(favorite: false, started: true);

        Assert.True(NotificationHelper.KeepsFor(null, new NotificationSubject(NotificationEvents.ItemAdded, SeriesId: Show), Now, Alice, watching));
        Assert.Equal(0, watching.StartedAsked);
    }

    [Fact]
    public void ARequestNotificationSkipsWhoTurnedRequestsOff()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var mine = new NotificationPreferences();
        mine.Events[NotificationEvents.SeerrRequests] = false;
        var preferences = new Dictionary<Guid, NotificationPreferences> { [alice] = mine };

        var kept = NotificationHelper.KeptBy(
            [new Db.DeviceToken { UserId = alice, Token = "a" }, new Db.DeviceToken { UserId = bob, Token = "b" }],
            NotificationEvents.SeerrRequests,
            preferences,
            Now);

        Assert.Equal(["b"], kept.Select(device => device.Token));
    }

    [Fact]
    public void ANotificationAboutNothingInParticularGoesToEveryDevice()
    {
        var mine = new NotificationPreferences { Pause = new NotificationPause() };
        var alice = Guid.NewGuid();

        var kept = NotificationHelper.KeptBy(
            [new Db.DeviceToken { UserId = alice, Token = "a" }],
            eventKey: null,
            new Dictionary<Guid, NotificationPreferences> { [alice] = mine },
            Now);

        Assert.Single(kept);
    }
}
