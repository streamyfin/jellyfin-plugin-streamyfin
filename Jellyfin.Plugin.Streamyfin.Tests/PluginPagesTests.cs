using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Page ordering, which decides two things at once: the page the dashboard opens the
/// plugin on, and the page its entry in the left menu points at.
/// </summary>
public class PluginPagesTests
{
    private static List<PluginPageInfo> Pages() =>
    [
        new() { Name = "Application" },
        new() { Name = "Application.js" },
        new() { Name = "Notifications" },
        new() { Name = "Yaml" }
    ];

    /// <summary>
    /// Every page the plugin serves is embedded in the assembly under the path it claims.
    /// </summary>
    /// <remarks>
    /// A page is registered by an <c>EmbeddedResourcePath</c> string, so a wrong one is
    /// not a build error: the dashboard serves an empty tab and nothing says why. The
    /// list is the plugin's own rather than one repeated here, so a page added without
    /// its resource fails, and so does a resource renamed without its page.
    ///
    /// <para>
    /// This exists because of the targeting screen: P1.2 to P1.4 built groups and per
    /// user overrides with an API, tests and no screen at all, which left an
    /// administrator hand crafting HTTP requests to use any of it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPageResourceIsEmbedded()
    {
        var assembly = typeof(StreamyfinPlugin).Assembly;
        var embedded = assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);

        var plugin = (IHasWebPages)FormatterServices_CreateUninitialized();

        var missing = plugin.GetPages()
            .Select(page => page.EmbeddedResourcePath)
            .Where(path => !embedded.Contains(path!))
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The pages that carry the admin interface are all served. Enumerating the plugin's
    /// own list proves each path exists; it cannot prove a page was never dropped from
    /// it, and a tab that quietly stops being registered is exactly as invisible as one
    /// pointing at a missing resource.
    /// </summary>
    [Theory]
    [InlineData("Application")]
    [InlineData("Targeting")]
    [InlineData("Notifications")]
    [InlineData("Other")]
    [InlineData("Yaml")]
    public void ThePageIsServed(string name)
    {
        var plugin = (IHasWebPages)FormatterServices_CreateUninitialized();
        var served = plugin.GetPages().Select(page => page.Name).ToArray();

        Assert.Contains(name, served);
        Assert.Contains(name + ".js", served);
    }

    /// <summary>
    /// With no preference the declared order stands.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoHomePageLeavesTheOrderAlone(string? homePage)
    {
        var ordered = StreamyfinPlugin.OrderedPages(Pages(), homePage);

        Assert.Equal(
            ["Application", "Application.js", "Notifications", "Yaml"],
            ordered.Select(p => p.Name));
    }

    /// <summary>
    /// The chosen page comes first, and every other page is still served. The dashboard
    /// opens the plugin on the first page it is given, so this setting is the whole
    /// mechanism behind it.
    /// </summary>
    [Fact]
    public void TheChosenHomePageComesFirst()
    {
        var ordered = StreamyfinPlugin.OrderedPages(Pages(), "Yaml");

        Assert.Equal(
            ["Yaml", "Application", "Application.js", "Notifications"],
            ordered.Select(p => p.Name));
    }

    /// <summary>
    /// A home page naming something that no longer exists serves the pages in their
    /// declared order rather than serving none. A renamed page or a typo in the YAML
    /// should cost the preference, not the admin interface.
    /// </summary>
    [Fact]
    public void AnUnknownHomePageFallsBackToTheDeclaredOrder()
    {
        var ordered = StreamyfinPlugin.OrderedPages(Pages(), "APageThatWasRenamed");

        Assert.Equal(
            ["Application", "Application.js", "Notifications", "Yaml"],
            ordered.Select(p => p.Name));
    }

    /// <summary>
    /// Exactly one page asks for a menu entry, and it is the one the admin lands on.
    /// The dashboard renders one entry per page that asks, so marking them all would
    /// put four Streamyfin rows in the drawer.
    /// </summary>
    [Fact]
    public void OnlyTheLandingPageAsksForAMenuEntry()
    {
        var plugin = (IHasWebPages)FormatterServices_CreateUninitialized();

        var pages = plugin.GetPages().ToList();
        var inMenu = pages.Where(p => p.EnableInMainMenu).ToList();

        Assert.Single(inMenu);
        Assert.Equal(pages[0].Name, inMenu[0].Name);
        Assert.Equal(StreamyfinPlugin.MenuDisplayName, inMenu[0].DisplayName);
        Assert.Equal(StreamyfinPlugin.MenuIcon, inMenu[0].MenuIcon);
    }

    // The plugin's constructor needs the server, which a unit test has no business
    // starting. GetPages only reads Instance through a null conditional, so an
    // uninitialized instance answers the question this test asks.
    private static object FormatterServices_CreateUninitialized() =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StreamyfinPlugin));
}
