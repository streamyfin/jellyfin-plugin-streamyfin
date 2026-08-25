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
