using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What a device says it can show, kept with its registration.
/// </summary>
public class DeviceCapabilitiesTests : IDisposable
{
    private readonly string _directory = TestDirectory.Create();
    private readonly PluginDatabase _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceCapabilitiesTests"/> class.
    /// </summary>
    public DeviceCapabilitiesTests()
    {
        _db = new PluginDatabase(_directory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try { System.IO.Directory.Delete(_directory, recursive: true); } catch (System.IO.IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A device that said nothing, or something unreadable, is taken to show no channel and no
    /// buttons.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{broken")]
    public void ADeviceThatSaidNothingCanShowNothingMore(string? stored)
    {
        Assert.Null(DeviceCapabilities.Read(stored));
    }

    /// <summary>
    /// What a device can show is read back as it was written.
    /// </summary>
    [Fact]
    public void WhatIsWrittenReadsBack()
    {
        var read = DeviceCapabilities.Read(DeviceCapabilities.Write(new DeviceCapabilities { Channels = 1, Categories = 1 }));

        Assert.True(read!.HasChannels);
        Assert.True(read.HasCategories);
    }

    /// <summary>
    /// A registration keeps what the device said it can show.
    /// </summary>
    [Fact]
    public void ARegistrationKeepsWhatTheDeviceCanShow()
    {
        var device = Guid.NewGuid();
        var user = Guid.NewGuid();
        _db.AddDeviceToken(new DeviceToken
        {
            DeviceId = device,
            UserId = user,
            Token = "ExponentPushToken[a]",
            Capabilities = new DeviceCapabilities { Channels = 1, Categories = 1 }
        });

        var stored = _db.GetUserDeviceTokens(user).Single();

        Assert.True(stored.Capabilities!.HasChannels);
        Assert.True(stored.Capabilities.HasCategories);
    }

    /// <summary>
    /// Two devices of one person, only one of which has the channels, are written for apart.
    /// </summary>
    [Fact]
    public void DevicesThatShowDifferentThingsAreWrittenForApart()
    {
        var withChannels = new DeviceToken { Token = "a", Capabilities = new DeviceCapabilities { Channels = 1 } };
        var without = new DeviceToken { Token = "b" };

        var audiences = NotificationHelper.ByAudience([withChannels, without]);

        Assert.Equal(2, audiences.Count);
        Assert.Contains(audiences, group => group.Audience.Channels && group.Tokens.SequenceEqual(["a"]));
        Assert.Contains(audiences, group => !group.Audience.Channels && group.Tokens.SequenceEqual(["b"]));
    }
}
