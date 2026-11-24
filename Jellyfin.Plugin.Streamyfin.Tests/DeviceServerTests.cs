using System;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The address a device reaches the server at, and the poster a notification shows.
/// </summary>
public class DeviceServerTests
{
    private static readonly Guid Item = Guid.Parse("c0ffee00-1234-4bcd-8ef0-1234567890ab");

    /// <summary>
    /// An address is stored without its trailing slash, so two devices that spell it
    /// differently are one address.
    /// </summary>
    [Theory]
    [InlineData("https://jellyfin.example.com", "https://jellyfin.example.com")]
    [InlineData("https://jellyfin.example.com/", "https://jellyfin.example.com")]
    [InlineData("  http://10.0.0.5:8096/  ", "http://10.0.0.5:8096")]
    [InlineData("https://example.com/jellyfin/", "https://example.com/jellyfin")]
    public void AnAddressIsStoredWithoutItsTrailingSlash(string sent, string stored)
    {
        Assert.Equal(stored, DeviceServer.Stored(sent));
    }

    /// <summary>
    /// What is not an address this can fetch from is no address.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jellyfin.example.com")]
    [InlineData("ftp://jellyfin.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/Items/1/Images/Primary")]
    public void WhatIsNotAnAddressIsNoAddress(string? sent)
    {
        Assert.Null(DeviceServer.Stored(sent));
    }

    /// <summary>
    /// A poster is an item's primary image on the device's own server.
    /// </summary>
    [Fact]
    public void APosterIsTheItemsPrimaryImageOnThatDeviceSserver()
    {
        Assert.Equal(
            "https://jellyfin.example.com/Items/c0ffee0012344bcd8ef01234567890ab/Images/Primary?maxHeight=640",
            DeviceServer.PosterOf("https://jellyfin.example.com/", Item));
    }

    /// <summary>
    /// A notification posted to the endpoint carries its own image, since whoever posts it
    /// knows where the image is and the server does not.
    /// </summary>
    [Fact]
    public void ANotificationPostedToTheEndpointCarriesItsImage()
    {
        var posted = new Notification
        {
            Title = "Something happened",
            Body = "Here it is",
            Image = " https://posters.example.com/a.jpg "
        };

        Assert.Equal("https://posters.example.com/a.jpg", posted.ToExpoNotification().RichContent?.Image);
    }

    /// <summary>
    /// What a phone could not fetch is dropped rather than sent: Expo refuses the whole
    /// message for it, so one bad address would cost the notification.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("poster.jpg")]
    [InlineData("file:///tmp/poster.jpg")]
    public void AnImageAPhoneCouldNotFetchIsDropped(string? image)
    {
        Assert.Null(new Notification { Body = "Here it is", Image = image }.ToExpoNotification().RichContent);
    }

    /// <summary>
    /// Without an address, or without an item, there is no poster rather than a broken one.
    /// </summary>
    [Fact]
    public void WithoutAnAddressThereIsNoPoster()
    {
        Assert.Null(DeviceServer.PosterOf(null, Item));
        Assert.Null(DeviceServer.PosterOf("not an address", Item));
        Assert.Null(DeviceServer.PosterOf("https://jellyfin.example.com", Guid.Empty));
    }
}
