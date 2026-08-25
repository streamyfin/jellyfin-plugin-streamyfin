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
    /// A shim goes on the same action as the route it stands in for, never on a second
    /// method that delegates. Two methods drift: one gets a fix, the other does not, and
    /// the shim quietly stops behaving like the thing it shims.
    /// </summary>
    [Fact]
    public void AShimSharesTheActionItStandsInFor()
    {
        foreach (var legacy in _legacyRoutes)
        {
            var methods = Routes()
                .Where(r => r.Template == legacy)
                .Select(r => r.Method)
                .Distinct()
                .ToArray();

            Assert.All(
                methods,
                method => Assert.Contains(
                    Routes().Where(r => r.Method == method),
                    r => r.Template.StartsWith("v1/", StringComparison.Ordinal)));
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
}
