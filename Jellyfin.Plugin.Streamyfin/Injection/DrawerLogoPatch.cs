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
    /// Injects the stylesheet into the web client's entry point.
    /// </summary>
    /// <param name="payload">The file as it stands.</param>
    /// <returns>The file with the stylesheet before its closing head tag.</returns>
    /// <remarks>
    /// The signature is fixed by File Transformation, which passes the payload and
    /// nothing else. The rule itself is <see cref="Inject"/>, which takes the row it
    /// aims at, so it can be exercised without a running server.
    /// </remarks>
    public static string IndexHtml(FileTransformationPayload payload) =>
        Inject(payload?.Contents, StreamyfinPlugin.LandingPageName);

    /// <summary>
    /// Puts the stylesheet in a document, aimed at one drawer row.
    /// </summary>
    /// <param name="contents">The document.</param>
    /// <param name="landingPage">
    /// The page the drawer row points at, which follows <c>Other.HomePage</c>. Nothing is
    /// injected without one: a rule aimed at a row that is not rendered would fail
    /// silently, with no logo and nothing to say why.
    /// </param>
    /// <returns>The document, patched or unchanged.</returns>
    internal static string Inject(string? contents, string? landingPage)
    {
        var document = contents ?? string.Empty;

        if (document.Length == 0 || string.IsNullOrWhiteSpace(landingPage))
        {
            return document;
        }

        if (document.Contains(Marker, StringComparison.Ordinal))
        {
            // Already patched. The transformation runs per request, and appending a
            // second copy on every page load would grow the document without bound.
            return document;
        }

        return Regex.Replace(document, "(</head>)", Style(landingPage) + "$1", RegexOptions.IgnoreCase);
    }

    private const string Marker = "streamyfin-drawer-logo";

    private static string Style(string landingPage)
    {
        var version = typeof(StreamyfinPlugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var id = StreamyfinPlugin.PluginId.ToString("N", CultureInfo.InvariantCulture);

        // Jellyfin already serves the logo, from imagePath in the plugin's meta.json.
        // The version is part of the route: the id alone answers 405.
        var image = string.Create(
            CultureInfo.InvariantCulture,
            $"/Plugins/{id}/{version}/Image");

        // Scoped to the drawer's plugin list, which jellyfin-web labels
        // plugins-subheader, so the rule cannot reach a link anywhere else in the app.
        // Inside that list it still matches on the page name, because the drawer renders
        // nothing identifying the plugin. Another plugin with a page of the same name,
        // also asking for a drawer row, would pick this up. The cost is a wrong icon.
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            <style id="{{Marker}}">
            [aria-labelledby="plugins-subheader"] a[href*="configurationpage?name={{landingPage}}"] .MuiIcon-root {
                font-size: 0;
                width: 1.5rem;
                height: 1.5rem;
                background: center / contain no-repeat url("{{image}}");
            }
            </style>

            """);
    }
}
