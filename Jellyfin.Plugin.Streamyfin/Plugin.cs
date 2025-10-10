using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Storage;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

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
    public StreamyfinPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Database = new Storage.Database(applicationPaths.DataPath);
        _prefix = GetType().Namespace;
    }
    
    public Storage.Database Database { get; }

    /// <inheritdoc />
    public override string Name => "Streamyfin";

    private static string? _prefix;

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("1e9e5d38-6e67-4615-8719-e98a5c34f004");

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

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        if (Instance?.Configuration?.Config?.Other?.HomePage != null)
        {
            var homePage = _pages().FirstOrDefault(page => string.Equals(page.Name, Instance.Configuration.Config.Other.HomePage, StringComparison.Ordinal));

            if (homePage != null)
            {
                List<PluginPageInfo> pages = [homePage];
                pages.AddRange(_pages().Where(p => p.Name != homePage.Name));

                foreach (var pluginPageInfo in pages)
                {
                    yield return pluginPageInfo;
                }
            }
            else
            {
                foreach (var pluginPageInfo in _pages())
                {
                    yield return pluginPageInfo;
                }
            }
        }
        else
        {
            foreach (var pluginPageInfo in _pages())
            {
                yield return pluginPageInfo;
            }
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
