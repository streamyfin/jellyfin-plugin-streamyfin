using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What the notifications route sends, and what it answers. The route itself cannot be built
/// in a test, so the two decisions it makes are read on their own.
/// </summary>
public class NotificationRouteTests
{
    /// <summary>
    /// Only the devices of an account that exists and is not disabled are sent to.
    /// </summary>
    /// <remarks>
    /// The route took every registered device, or a named account's, as it found them: a
    /// disabled account, or a deleted one whose devices nothing removed, went on hearing what
    /// integrations and Seerr relayed, where every other path tells it nothing.
    /// </remarks>
    [Fact]
    public void OnlyAccountsThatMayBeToldAreSentTo()
    {
        var enabled = Account();
        var disabled = Account();
        disabled.Permissions.Add(new Permission(PermissionKind.IsDisabled, true));
        var deleted = Guid.NewGuid();
        var accounts = new Dictionary<Guid, User> { [enabled.Id] = enabled, [disabled.Id] = disabled };

        var tokens = new[]
        {
            new DeviceToken { DeviceId = Guid.NewGuid(), Token = "ExponentPushToken[enabled]", UserId = enabled.Id },
            new DeviceToken { DeviceId = Guid.NewGuid(), Token = "ExponentPushToken[disabled]", UserId = disabled.Id },
            new DeviceToken { DeviceId = Guid.NewGuid(), Token = "ExponentPushToken[deleted]", UserId = deleted },
            new DeviceToken { DeviceId = Guid.NewGuid(), Token = "ExponentPushToken[nobody]", UserId = Guid.Empty },
        };

        var reached = NotificationHelper.Reachable(tokens, id => accounts.GetValueOrDefault(id));

        Assert.Equal(["ExponentPushToken[enabled]"], reached.Select(token => token.Token));
    }

    /// <summary>
    /// A send Expo took none of answers 502 and says so.
    /// </summary>
    /// <remarks>
    /// It answered 200 with the body "null", so Seerr and the integrations took a send that
    /// reached nobody for a delivered one.
    /// </remarks>
    [Fact]
    public void ASendExpoTookNoneOfAnswersBadGateway()
    {
        var refused = StreamyfinController.Unsent(null);

        Assert.NotNull(refused);
        Assert.Equal(StatusCodes.Status502BadGateway, refused!.StatusCode);
        Assert.Equal("Expo did not take the notifications, so nobody was told. The server log has its answer.", refused.Value);
        Assert.Null(StreamyfinController.Unsent(new ExpoNotificationResponse()));
    }

    private static User Account()
    {
        var user = new User("zz-test", "provider", "reset") { Id = Guid.NewGuid() };
        user.Permissions.Add(new Permission(PermissionKind.IsDisabled, false));
        return user;
    }
}
