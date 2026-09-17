using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The language a device says it is in, which is what its notifications are written in.
/// </summary>
/// <remarks>
/// It comes from a client, so it is whatever that client sends. A tag that is not a
/// language is stored as none rather than refused: a device that cannot say what language
/// it is in should still receive its notifications, in the server's.
/// </remarks>
public class DeviceLanguageTests
{
    /// <summary>
    /// A tag is stored the way .NET names it, so two devices that spell the same language
    /// differently are one language at send time.
    /// </summary>
    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData("FR-fr", "fr-FR")]
    [InlineData("  es-MX  ", "es-MX")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("fr_FR", "fr-FR")]
    public void ALanguageIsStoredTheWayItIsNamed(string sent, string stored)
    {
        Assert.Equal(stored, DeviceLanguage.Stored(sent));
    }

    /// <summary>
    /// Nothing, or something that is not a language tag, is no language.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the user's language")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void WhatIsNotALanguageIsNoLanguage(string? sent)
    {
        Assert.Null(DeviceLanguage.Stored(sent));
    }

    /// <summary>
    /// Devices are grouped by the language they asked for, so one message is written per
    /// language rather than one per device.
    /// </summary>
    [Fact]
    public void DevicesAreGroupedByTheLanguageTheyAskedFor()
    {
        var groups = NotificationHelper.ByLanguage([
            Device("one", "fr-FR"),
            Device("two", "FR-fr"),
            Device("three", "es-MX"),
            Device("four", null),
            Device("five", "   ")
        ]);

        Assert.Equal(3, groups.Count);
        Assert.Equal(["one", "two"], groups.Single(group => group.Culture?.Name == "fr-FR").Tokens);
        Assert.Equal(["three"], groups.Single(group => group.Culture?.Name == "es-MX").Tokens);
        Assert.Equal(["four", "five"], groups.Single(group => group.Culture is null).Tokens);
    }

    /// <summary>
    /// A device that asked for nothing is its own group, written in the server's language.
    /// </summary>
    [Fact]
    public void ADeviceThatAskedForNothingIsWrittenInTheServersLanguage()
    {
        var groups = NotificationHelper.ByLanguage([Device("one", null)]);

        Assert.Null(Assert.Single(groups).Culture);
    }

    /// <summary>
    /// A token registered twice is sent to once, whatever the rows say.
    /// </summary>
    [Fact]
    public void ATokenOnTwoRowsIsOneRecipient()
    {
        var groups = NotificationHelper.ByLanguage([Device("same", "fr"), Device("same", "fr")]);

        Assert.Equal(["same"], Assert.Single(groups).Tokens);
    }

    private static DeviceToken Device(string token, string? language) => new()
    {
        DeviceId = System.Guid.NewGuid(),
        UserId = System.Guid.NewGuid(),
        Token = token,
        Language = language
    };

    /// <summary>
    /// The culture a message is written in, or none, which leaves the server's.
    /// </summary>
    [Fact]
    public void TheCultureIsTheOneTheDeviceNamed()
    {
        Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), DeviceLanguage.CultureOf("fr-FR"));
        Assert.Null(DeviceLanguage.CultureOf("the user's language"));
        Assert.Null(DeviceLanguage.CultureOf(null));
    }
}
