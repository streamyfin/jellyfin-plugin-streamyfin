using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Streamyfin.Injection;

/// <summary>
/// Puts the plugin's own logo on its row in the dashboard drawer.
/// </summary>
/// <remarks>
/// The drawer renders the icon as <c>&lt;Icon&gt;{MenuIcon}&lt;/Icon&gt;</c>, MUI's icon
/// font component, so <c>MenuIcon</c> can only ever be a Material ligature. The only way
/// to show an image is to reach the page from outside, which is what File Transformation
/// exists for.
///
/// <para>
/// This is deliberately CSS and not script. The drawer is a React tree that re-renders,
/// and a script that rewrote the DOM would have its work thrown away on the next render
/// unless it kept a MutationObserver alive. A stylesheet survives every render.
/// </para>
/// </remarks>
public static class DrawerLogoPatch
{
    /// <summary>
    /// Where the drawer row for the landing page points. See <c>getPluginUrl</c> in
    /// <c>src/utils/dashboard.js</c>, which builds <c>configurationpage?name=</c> plus the
    /// page name.
    /// </summary>
    private const string LandingPageName = "Application";

    /// <summary>
    /// Injects the stylesheet into the web client's entry point.
    /// </summary>
    /// <param name="payload">The file as it stands.</param>
    /// <returns>The file with the stylesheet before its closing head tag.</returns>
    public static string IndexHtml(FileTransformationPayload payload)
    {
        var contents = payload?.Contents ?? string.Empty;

        if (contents.Length == 0 || contents.Contains(Marker, StringComparison.Ordinal))
        {
            // Already patched. The transformation runs per request, and appending a
            // second copy on every page load would grow the document without bound.
            return contents;
        }

        return Regex.Replace(contents, "(</head>)", Style() + "$1", RegexOptions.IgnoreCase);
    }

    private const string Marker = "streamyfin-drawer-logo";

    private static string Style()
    {
        var version = typeof(StreamyfinPlugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var id = StreamyfinPlugin.PluginId.ToString("N", CultureInfo.InvariantCulture);

        // Jellyfin already serves the logo, from imagePath in the plugin's meta.json.
        // The version is part of the route: the id alone answers 405.
        var image = string.Create(
            CultureInfo.InvariantCulture,
            $"/Plugins/{id}/{version}/Image");

        // Matched on the link rather than on anything identifying the plugin, because the
        // drawer renders nothing else to go on. A page called Application belonging to
        // another plugin, and also asking for a drawer row, would pick this up. The cost
        // of that collision is a wrong icon.
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            <style id="{{Marker}}">
            a[href*="configurationpage?name={{LandingPageName}}"] .MuiIcon-root {
                font-size: 0;
                width: 1.5rem;
                height: 1.5rem;
                background: center / contain no-repeat url("{{image}}");
            }
            </style>

            """);
    }
}
