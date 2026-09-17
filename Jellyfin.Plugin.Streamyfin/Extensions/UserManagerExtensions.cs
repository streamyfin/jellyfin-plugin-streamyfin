using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Streamyfin.Db;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Streamyfin.Extensions;

public static class UserManagerExtensions
{
    public static List<DeviceToken> GetAdminDeviceTokens(this IUserManager? manager) => (
        manager?.GetUsers()
            // A disabled administrator is refused by Jellyfin and is told nothing here either.
            .Where(u => u.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value) && !u.IsDisabled())
            .SelectMany(u =>
                StreamyfinPlugin.Instance?.Database.GetUserDeviceTokens(u.Id) ?? Enumerable.Empty<DeviceToken>()) 
        ?? Array.Empty<DeviceToken>()
    ).ToList();

    public static List<string> GetAdminTokens(this IUserManager? manager) => 
        manager?.GetAdminDeviceTokens().Select(deviceToken => deviceToken.Token).ToList() ?? [];

    /// <summary>
    /// Whether an account has been disabled, which Jellyfin refuses on every request.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <returns>True when the account holds the disabled permission.</returns>
    public static bool IsDisabled(this User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Permissions.Any(p => p.Kind == PermissionKind.IsDisabled && p.Value);
    }

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