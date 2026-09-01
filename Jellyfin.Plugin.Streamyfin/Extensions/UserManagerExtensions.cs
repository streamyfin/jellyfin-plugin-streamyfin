using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Streamyfin.Db;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Streamyfin.Extensions;

public static class UserManagerExtensions
{
    public static List<DeviceToken> GetAdminDeviceTokens(this IUserManager? manager) => (
        manager?.GetUsers()
            .Where(u => u.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value))
            .SelectMany(u =>
                StreamyfinPlugin.Instance?.Database.GetUserDeviceTokens(u.Id) ?? Enumerable.Empty<DeviceToken>()) 
        ?? Array.Empty<DeviceToken>()
    ).ToList();

    public static List<string> GetAdminTokens(this IUserManager? manager) => 
        manager?.GetAdminDeviceTokens().Select(deviceToken => deviceToken.Token).ToList() ?? [];

    /// <summary>
    /// Whether a user administers this server.
    /// </summary>
    /// <param name="manager">The user manager.</param>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <returns>True when the user holds the administrator permission.</returns>
    /// <remarks>
    /// Read from the permission rather than from a role claim, because the same
    /// question is asked here and in <c>GetAdminDeviceTokens</c> and two different
    /// answers to it would be a security bug rather than an inconsistency.
    /// </remarks>
    public static bool IsAdministrator(this IUserManager? manager, Guid userId) =>
        // An empty id is not a user with no permissions, it is the absence of a user,
        // which is what an API key call looks like. GetUserById throws on it.
        !userId.Equals(default) &&
        manager?.GetUserById(userId)?
            .Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value) == true;
}