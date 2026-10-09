using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Streamyfin.Injection;

/// <summary>
/// Puts the plugin's mark on its row in the dashboard drawer, in the drawer's own colour.
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

    /// <summary>
    /// The logo drawn the way the drawer's Material icons are: one colour, on their 24 px
    /// grid, its outline at their 2 px. The play triangle as a line, the wave filled below
    /// it.
    /// </summary>
    /// <remarks>
    /// The logo in colour, which this used to show, was the only coloured thing in a list of
    /// white glyphs. The mark is a mask filled with the row's own colour, so it is white on
    /// the dark theme, dark on the light one, and follows the row when it is hovered or
    /// selected, as the glyphs beside it do.
    /// </remarks>
    internal const string Mark =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">"
        + "<defs><clipPath id=\"in\"><path d=\"M8.83 4.04L18.63 9.36A3 3 0 0 1 18.63 14.64L8.83 19.96A3 3 0 0 1 4.4 17.33L4.4 6.67A3 3 0 0 1 8.83 4.04Z\"/></clipPath></defs>"
        + "<path d=\"M8.83 4.04L18.63 9.36A3 3 0 0 1 18.63 14.64L8.83 19.96A3 3 0 0 1 4.4 17.33L4.4 6.67A3 3 0 0 1 8.83 4.04Z\" fill=\"none\" stroke=\"#000\" stroke-width=\"2\" stroke-linejoin=\"round\"/>"
        + "<path d=\"M4.4 10.67C6.8 9.67 8.8 10.07 10.8 11.97S14.95 15.16 17.95 15.01L21.95 23.01L0.4 23.01L0.4 10.67Z\" clip-path=\"url(#in)\"/>"
        + "</svg>";

    private static string Style(string landingPage)
    {
        var mark = "data:image/svg+xml," + Uri.EscapeDataString(Mark);

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
                background-color: currentColor;
                -webkit-mask: url("{{mark}}") center / contain no-repeat;
                mask: url("{{mark}}") center / contain no-repeat;
            }
            </style>

            """);
    }
}
