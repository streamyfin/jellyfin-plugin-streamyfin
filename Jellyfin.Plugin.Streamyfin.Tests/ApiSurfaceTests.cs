using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.Api;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The routes the plugin serves.
///
/// P1.6 put every route under a <c>v1/</c> prefix and kept the path it has always had
/// as a shim, so that the next change to this surface is a choice rather than a
/// breaking one. That promise is only worth something if something checks it: a route
/// dropped from a shim is not a failure anything else notices until an app in the
/// field hits a 404, months later, on a version nobody is testing.
/// </summary>
public class ApiSurfaceTests
{
    /// <summary>
    /// Every path that has ever been served, and still must be.
    /// </summary>
    /// <remarks>
    /// Removing an entry from this list is how a route stops being supported. It should
    /// take a deliberate edit and a note about which app versions are being cut off,
    /// rather than falling out of a refactor.
    /// </remarks>
    private static readonly string[] _legacyRoutes =
    [
        "config",
        "config/default",
        "config/resolved",
        "config/schema",
        "config/yaml",
        "device",
        "device/{deviceId}",
        "groups",
        "groups/{id}",
        "groups/{id}/members",
        "notification",
        "users/{userId}/settings"
    ];

    private static IEnumerable<(MethodInfo Method, string Template)> Routes() =>
        typeof(StreamyfinController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(
                method => method.GetCustomAttributes<HttpMethodAttribute>(),
                (method, attribute) => (method, attribute.Template))
            .Where(pair => pair.Template is not null)
            .Select(pair => (pair.method, pair.Template!));

    /// <summary>
    /// Every route the plugin serves is reachable under the version prefix, so a client
    /// written today never has to use an unversioned path.
    /// </summary>
    [Fact]
    public void EveryActionIsReachableUnderTheVersionPrefix()
    {
        var withoutVersioned = Routes()
            .GroupBy(r => r.Method)
            .Where(group => !group.Any(r => r.Template.StartsWith("v1/", StringComparison.Ordinal)))
            .Select(group => group.Key.Name)
            .ToArray();

        Assert.Empty(withoutVersioned);
    }

    /// <summary>
    /// Every path that was ever served still is. This is the shim promise, and it is the
    /// whole reason the prefix could be introduced without a flag day.
    /// </summary>
    [Fact]
    public void EveryPathThatWasEverServedStillIs()
    {
        var served = Routes().Select(r => r.Template).ToHashSet(StringComparer.Ordinal);

        var missing = _legacyRoutes.Where(route => !served.Contains(route)).ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A shim goes on the same action as the route it stands in for, and that action
    /// answers the same path under the prefix. Asserting only that the action has some
    /// versioned route would pass for an action carrying an unrelated one.
    /// </summary>
    /// <remarks>
    /// Same action rather than a second method that delegates. Two methods drift: one
    /// gets a fix, the other does not, and the shim quietly stops behaving like the
    /// thing it shims.
    /// </remarks>
    [Fact]
    public void AShimAnswersTheSamePathUnderThePrefix()
    {
        var routes = Routes().ToList();

        foreach (var legacy in _legacyRoutes)
        {
            var actions = routes
                .Where(r => r.Template == legacy)
                .Select(r => r.Method)
                .Distinct()
                .ToArray();

            Assert.NotEmpty(actions);

            foreach (var action in actions)
            {
                var templates = routes
                    .Where(r => r.Method == action)
                    .Select(r => r.Template)
                    .ToArray();

                Assert.Contains($"v1/{legacy}", templates);
            }
        }
    }

    /// <summary>
    /// The names that were singular and should not have been keep working, under both the
    /// old path and the version prefix, next to the plural they should have had.
    /// </summary>
    [Theory]
    [InlineData("device", "devices")]
    [InlineData("device/{deviceId}", "devices/{deviceId}")]
    [InlineData("notification", "notifications")]
    public void ARenamedRouteKeepsItsOldNameToo(string singular, string plural)
    {
        var served = Routes().Select(r => r.Template).ToHashSet(StringComparer.Ordinal);

        Assert.Contains($"v1/{plural}", served);
        Assert.Contains($"v1/{singular}", served);
        Assert.Contains(singular, served);
    }

    /// <summary>
    /// Posting a notification takes an administrator.
    /// </summary>
    /// <remarks>
    /// A notification with no target goes to every registered device, so with a plain
    /// <c>Authorize</c> any account on the server could push to everyone. The app never
    /// calls this route: it registers and removes its own device and nothing else. An
    /// API key counts as an administrator in Jellyfin, so integrations keep working.
    /// </remarks>
    [Fact]
    public void PostingANotificationRequiresElevation()
    {
        var method = typeof(StreamyfinController).GetMethod(nameof(StreamyfinController.PostNotifications));

        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(MediaBrowser.Common.Api.Policies.RequiresElevation, authorize!.Policy);
    }

    /// <summary>
    /// Probing an address the caller chose is for administrators.
    /// </summary>
    /// <remarks>
    /// The route makes the server open an address the caller names and reports the
    /// status code and the version it read back. Relaxed to a plain Authorize it would
    /// answer that for any account on the server.
    /// </remarks>
    [Fact]
    public void ProbingAnIntegrationRequiresElevation()
    {
        var method = typeof(StreamyfinController).GetMethod(nameof(StreamyfinController.ProbeIntegration));

        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(MediaBrowser.Common.Api.Policies.RequiresElevation, authorize!.Policy);
    }

    /// <summary>
    /// A backup, and putting one back, are both for administrators.
    /// </summary>
    /// <remarks>
    /// The file carries the Seerr admin key, and restoring one replaces every setting
    /// on the server.
    /// </remarks>
    /// <param name="route">The method on the controller.</param>
    [Theory]
    [InlineData(nameof(StreamyfinController.GetBackup))]
    [InlineData(nameof(StreamyfinController.Restore))]
    public void BackupIsForAdministrators(string route)
    {
        var method = typeof(StreamyfinController).GetMethod(route);

        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(MediaBrowser.Common.Api.Policies.RequiresElevation, authorize!.Policy);
    }

    /// <summary>
    /// Reading the health of the integrations is for any signed in account, and for no
    /// one else.
    /// </summary>
    /// <remarks>
    /// The app changes what it offers by it, and the addresses probed are the ones
    /// resolved for the caller rather than ones they name, so there is nothing here to
    /// point anywhere.
    /// </remarks>
    [Fact]
    public void ReadingIntegrationHealthNeedsAnAccountAndNoMore()
    {
        var method = typeof(StreamyfinController).GetMethod(nameof(StreamyfinController.GetIntegrationHealth));

        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Null(authorize!.Policy);
    }
}
