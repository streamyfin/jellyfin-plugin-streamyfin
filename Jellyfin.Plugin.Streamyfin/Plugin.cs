using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Db;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin;

/// <summary>
/// The main plugin.
/// </summary>
public class StreamyfinPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    public StreamyfinPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Instance = this;
        Database = new PluginDatabase(applicationPaths.DataPath, loggerFactory.CreateLogger<PluginDatabase>());
        _prefix = GetType().Namespace;
    }

    /// <summary>
    /// Gets the plugin's database.
    /// </summary>
    public PluginDatabase Database { get; }

    /// <inheritdoc />
    public override string Name => "Streamyfin";

    private static string? _prefix;

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// The plugin's id, as a constant. Jellyfin serves the logo at
    /// <c>/Plugins/{id}/{version}/Image</c>, and the drawer stylesheet needs it before
    /// there is an instance to ask.
    /// </summary>
    internal static readonly Guid PluginId = Guid.Parse("1e9e5d38-6e67-4615-8719-e98a5c34f004");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static StreamyfinPlugin? Instance { get; private set; }

    private List<PluginPageInfo> _pages () =>
    [
        new()
        {
            Name = "Application",
            EmbeddedResourcePath = _prefix + ".Pages.Application.index.html"
        },

        new PluginPageInfo
        {
            Name = "Application.js",
            EmbeddedResourcePath = _prefix + ".Pages.Application.index.js"
        },

        new PluginPageInfo
        {
            Name = "Notifications",
            EmbeddedResourcePath = _prefix + ".Pages.Notifications.index.html"
        },

        new PluginPageInfo
        {
            Name = "Notifications.js",
            EmbeddedResourcePath = _prefix + ".Pages.Notifications.index.js"
        },

        new PluginPageInfo
        {
            Name = "Other",
            EmbeddedResourcePath = _prefix + ".Pages.Other.index.html"
        },

        new PluginPageInfo
        {
            Name = "Other.js",
            EmbeddedResourcePath = _prefix + ".Pages.Other.index.js"
        },

        new PluginPageInfo
        {
            Name = "Yaml",
            EmbeddedResourcePath = _prefix + ".Pages.YamlEditor.index.html"
        },

        new PluginPageInfo
        {
            Name = "Yaml.js",
            EmbeddedResourcePath = _prefix + ".Pages.YamlEditor.index.js"
        }
    ];

    /// <summary>
    /// The label the dashboard shows for the plugin's entry.
    /// </summary>
    internal const string MenuDisplayName = "Streamyfin";

    /// <summary>
    /// The dashboard's icon for that entry.
    /// </summary>
    /// <remarks>
    /// It has to be a Material ligature and cannot be the plugin's own logo.
    /// <c>PluginDrawerSection.tsx</c> in the web client renders it as
    /// <c>&lt;Icon&gt;{pageInfo.MenuIcon}&lt;/Icon&gt;</c>, which is the MUI icon font
    /// component, so anything that is not a glyph name comes out as literal text.
    /// The logo still shows where an image is allowed, on the plugin catalogue entry,
    /// through <c>imageUrl</c> in the manifest.
    ///
    /// <para>
    /// <c>devices</c> rather than something closer to the logo's play triangle: every
    /// glyph of that family reads as the YouTube mark, which this plugin is not. It
    /// also says what the plugin is for, configuring the Streamyfin app on a user's
    /// devices, which is more use in a drawer than a decorative shape.
    /// </para>
    /// </remarks>
    internal const string MenuIcon = "devices";

    /// <summary>
    /// The plugin's pages, the admin's landing page first.
    /// </summary>
    /// <returns>The pages, ordered.</returns>
    /// <remarks>
    /// The <c>Other.HomePage</c> setting names the page an administrator wants to land
    /// on. It has to come first because the dashboard opens the plugin on the first
    /// page it is given.
    /// </remarks>
    internal static List<PluginPageInfo> OrderedPages(List<PluginPageInfo> pages, string? homePageName)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (string.IsNullOrWhiteSpace(homePageName))
        {
            return pages;
        }

        var homePage = pages.Find(page => string.Equals(page.Name, homePageName, StringComparison.Ordinal));

        if (homePage is null)
        {
            // A page name that no longer exists, from a renamed page or a typo in the
            // YAML. Serving the pages in their declared order beats serving none.
            return pages;
        }

        List<PluginPageInfo> ordered = [homePage];
        ordered.AddRange(pages.Where(page => page.Name != homePage.Name));

        return ordered;
    }

    /// <summary>
    /// The page the drawer row points at, which is the page the dashboard opens.
    /// </summary>
    /// <remarks>
    /// Read by <see cref="GetPages"/> to decide which page asks for a menu row, and by
    /// <c>DrawerLogoPatch</c> to build a selector for that row. Two answers to the same
    /// question would mean a stylesheet aimed at a row that is not there, which is a
    /// silent failure: the logo simply never appears.
    /// </remarks>
    internal static string? LandingPageName
    {
        get
        {
            if (Instance is null)
            {
                return null;
            }

            var pages = OrderedPages(Instance._pages(), Instance.Configuration?.Config?.Other?.HomePage);

            return pages.Count > 0 ? pages[0].Name : null;
        }
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var pages = OrderedPages(_pages(), Instance?.Configuration?.Config?.Other?.HomePage);

        // The dashboard renders one entry per page that asks for one, so only the
        // landing page asks. Marking every page would put four Streamyfin entries in
        // the drawer, and marking a fixed one would ignore the home page setting.
        var landing = pages.Count > 0 ? pages[0] : null;

        foreach (var pluginPageInfo in pages)
        {
            if (ReferenceEquals(pluginPageInfo, landing))
            {
                pluginPageInfo.DisplayName = MenuDisplayName;
                pluginPageInfo.EnableInMainMenu = true;
                pluginPageInfo.MenuIcon = MenuIcon;
            }

            yield return pluginPageInfo;
        }

        // region pages

        // endregion pages
        
        // region libraries

        // region monaco-editor
        yield return new PluginPageInfo
        {
            Name = "yaml.worker.js",
            EmbeddedResourcePath = _prefix + ".Pages.Libraries.yaml.worker.js"
        };
        
        yield return new PluginPageInfo
        {
            Name = "json.worker.js",
            EmbeddedResourcePath = _prefix + ".Pages.Libraries.json.worker.js"
        };
                
        yield return new PluginPageInfo
        {
            Name = "editor.worker.js",
            EmbeddedResourcePath = _prefix + ".Pages.Libraries.editor.worker.js"
        };

        yield return new PluginPageInfo
        {
            Name = "monaco-editor.bundle.js",
            EmbeddedResourcePath = _prefix + ".Pages.Libraries.monaco-editor.bundle.js"
        };
        // endregion monaco-editor

        yield return new PluginPageInfo
        {
            Name = "json-editor.js",
            EmbeddedResourcePath = _prefix + ".Pages.Libraries.json-editor.min.js"
        };

        yield return new PluginPageInfo
        {
            Name = "js-yaml.js",
            EmbeddedResourcePath = _prefix + ".Pages.Libraries.js-yaml.min.js"
        };

        yield return new PluginPageInfo
        {
            Name = "shared.js",
            EmbeddedResourcePath = _prefix + ".Pages.shared.js"
        };
        // endregion libraries
    }
}
