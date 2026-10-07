using System;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The channel, thread and buttons a message carries, for the devices that show them.
/// </summary>
public class MessageMarksTests
{
    private static readonly NotificationSubject AnEpisode = new("itemAdded", Guid.NewGuid(), Guid.Parse("a656b907eb3a73532e40e44b968d0225"));

    [Fact]
    public void ADeviceThatShowsEverythingGetsEverything()
    {
        var message = new ExpoNotificationRequest();

        MessageMarks.Apply(AnEpisode, message, new Audience(null, null, Channels: true, Categories: true));

        Assert.Equal("new-content", message.ChannelId);
        Assert.Equal("episode", message.CategoryId);
        Assert.Equal("series-a656b907eb3a73532e40e44b968d0225", message.ThreadId);
    }

    // A channel the device never created would hide the notification altogether.
    [Fact]
    public void ADeviceThatSaidNothingGetsOnlyTheThread()
    {
        var message = new ExpoNotificationRequest();

        MessageMarks.Apply(AnEpisode, message, new Audience(null, null));

        Assert.Null(message.ChannelId);
        Assert.Null(message.CategoryId);
        Assert.Equal("series-a656b907eb3a73532e40e44b968d0225", message.ThreadId);
    }

    [Fact]
    public void TheThreadReachesExpoUnderItsName()
    {
        var message = new ExpoNotificationRequest { ThreadId = "new-content" };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(message);

        Assert.Contains("\"threadId\":\"new-content\"", json, StringComparison.Ordinal);
    }
}
