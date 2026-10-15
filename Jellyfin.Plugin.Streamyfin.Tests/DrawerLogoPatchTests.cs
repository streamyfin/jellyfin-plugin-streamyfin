using System;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Streamyfin.Injection;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The stylesheet that puts the plugin's logo on its drawer row.
///
/// The dashboard draws the row's icon itself: the Folder SVG on Jellyfin 10.11, whatever
/// <c>MenuIcon</c> says, and on 12 the Material ligature <c>MenuIcon</c> names, through
/// MUI's icon font. Neither takes an image, so the logo has to come from outside the
/// plugin's own pages. File Transformation is that outside.
/// </summary>
public class DrawerLogoPatchTests
{
    private const string LandingPage = "Application";

    private const string Document = "<html><head><title>Jellyfin</title></head><body></body></html>";

    /// <summary>
    /// The stylesheet lands inside the head, before it closes.
    /// </summary>
    [Fact]
    public void TheStyleGoesInsideTheHead()
    {
        var patched = DrawerLogoPatch.Inject(Document, LandingPage);

        var style = patched.IndexOf("<style id=\"streamyfin-drawer-logo\"", System.StringComparison.Ordinal);
        var headClose = patched.IndexOf("</head>", System.StringComparison.Ordinal);

        Assert.True(style > 0, "the stylesheet is missing");
        Assert.True(style < headClose, "the stylesheet landed outside the head");
    }

    /// <summary>
    /// Patching an already patched document changes nothing. The transformation runs on
    /// every request for the page, so appending each time would grow the document without
    /// bound for as long as the server is up.
    /// </summary>
    [Fact]
    public void PatchingTwiceIsTheSameAsPatchingOnce()
    {
        var once = DrawerLogoPatch.Inject(Document, LandingPage);
        var twice = DrawerLogoPatch.Inject(once, LandingPage);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// The mark is a mask filled with the row's colour, as the Material glyphs beside it
    /// are drawn, rather than the logo in colour, the one coloured thing in the list.
    /// </summary>
    [Fact]
    public void TheMarkTakesTheRowsColour()
    {
        var patched = DrawerLogoPatch.Inject(Document, LandingPage);

        Assert.Contains("background-color: currentColor;", patched, System.StringComparison.Ordinal);
        Assert.Contains("mask: url(\"data:image/svg+xml,", patched, System.StringComparison.Ordinal);
        Assert.Contains("-webkit-mask: url(\"data:image/svg+xml,", patched, System.StringComparison.Ordinal);
        Assert.DoesNotContain("/Image", patched, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// What the rule carries is the mark, whole, on Material's 24 px grid: escaped so it
    /// survives a CSS string, and readable once unescaped.
    /// </summary>
    [Fact]
    public void TheMarkTravelsWholeInTheRule()
    {
        var patched = DrawerLogoPatch.Inject(Document, LandingPage);
        var start = patched.IndexOf("data:image/svg+xml,", System.StringComparison.Ordinal) + "data:image/svg+xml,".Length;
        var end = patched.IndexOf('"', start);

        var svg = System.Xml.Linq.XDocument.Parse(System.Uri.UnescapeDataString(patched[start..end]));

        Assert.Equal(DrawerLogoPatch.Mark, System.Uri.UnescapeDataString(patched[start..end]));
        Assert.Equal("0 0 24 24", svg.Root!.Attribute("viewBox")!.Value);
        Assert.DoesNotContain("\"", patched[start..end], System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule targets the drawer row for the landing page, and only inside the drawer's
    /// plugin list. The page name follows <c>Other.HomePage</c>, so it is read from the
    /// same place the menu entry is decided rather than written down twice.
    /// </summary>
    [Fact]
    public void TheRuleTargetsTheDrawerRow()
    {
        var patched = DrawerLogoPatch.Inject(Document, LandingPage);

        Assert.Contains(
            $"configurationpage?name={LandingPage}",
            patched,
            System.StringComparison.Ordinal);
        Assert.Contains("[aria-labelledby=\"plugins-subheader\"]", patched, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The mark is drawn in the row's icon container, which both lines render, and the
    /// icon inside it is hidden: Jellyfin 12 draws <c>MenuIcon</c> with MUI's icon font,
    /// 10.11 the Folder SVG for every plugin. A rule aimed at one icon component missed
    /// the other line, as the icon font did on 10.11.
    /// </summary>
    [Fact]
    public void TheMarkTakesTheIconsPlaceWhicheverIconTheDrawerDraws()
    {
        var patched = DrawerLogoPatch.Inject(Document, LandingPage);

        Assert.Matches(@"\.MuiListItemIcon-root > \* \{\s*display: none;\s*\}", patched);
        Assert.Matches(@"\.MuiListItemIcon-root::before \{[^}]*content: """";[^}]*mask: url\(", patched);
        Assert.DoesNotContain(".MuiIcon-root", patched, StringComparison.Ordinal);
        Assert.DoesNotContain(".MuiSvgIcon-root", patched, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule matches the end of the row's link. Home Screen Sections, which asks for a
    /// drawer row of its own and needs File Transformation too, has a page whose name
    /// begins with <c>Home</c>, a page an administrator can land on. Matched anywhere in
    /// the link, that row took the mark.
    /// </summary>
    /// <param name="landingPage">The page the plugin's row points at.</param>
    /// <param name="otherRow">Another plugin's row, whose page name begins the same.</param>
    [Theory]
    [InlineData("Home", "#/configurationpage?name=Home%20Screen%20Sections")]
    [InlineData("Home", "#/configurationpage?name=HomeScreenCompanion")]
    [InlineData("Application", "#/configurationpage?name=ApplicationInsights")]
    public void ARowWhoseNameOnlyBeginsLikeTheLandingPageKeepsItsIcon(string landingPage, string otherRow)
    {
        var link = RowLink(DrawerLogoPatch.Inject(Document, landingPage));

        Assert.True(link.Matches($"#/configurationpage?name={landingPage}"), "the plugin's own row is missed");
        Assert.False(link.Matches(otherRow), $"{otherRow} takes the mark");
    }

    /// <summary>
    /// A contrast theme paints backgrounds over with the canvas colour, and the mark is a
    /// background: on 12 the row showed no icon at all. Opted out, the mark keeps the
    /// row's colour, which the theme sets.
    /// </summary>
    [Fact]
    public void TheMarkStaysVisibleInAContrastTheme()
    {
        var patched = DrawerLogoPatch.Inject(Document, LandingPage);

        Assert.Matches(@"::before \{[^}]*forced-color-adjust: preserve-parent-color;", patched);
    }

    /// <summary>
    /// The rule follows the home page setting rather than a fixed page. An administrator
    /// who lands on the YAML editor gets the row they actually have.
    /// </summary>
    [Fact]
    public void TheRuleFollowsTheHomePageSetting()
    {
        var patched = DrawerLogoPatch.Inject(Document, "Yaml");

        Assert.Contains("configurationpage?name=Yaml", patched, System.StringComparison.Ordinal);
        Assert.DoesNotContain("configurationpage?name=Application", patched, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// With no row to aim at, nothing is injected rather than a rule aimed at nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoLandingPageMeansNoInjection(string? landingPage)
    {
        Assert.Equal(Document, DrawerLogoPatch.Inject(Document, landingPage));
    }

    /// <summary>
    /// Nothing to patch is not a failure. A callback that threw would take the web client's
    /// entry point down with it, which is a far worse outcome than a Material icon.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingToPatchIsNotAFailure(string? contents)
    {
        Assert.Equal(string.Empty, DrawerLogoPatch.Inject(contents, LandingPage));
    }

    /// <summary>
    /// A document with no head is served unchanged rather than mangled.
    /// </summary>
    [Fact]
    public void ADocumentWithNoHeadIsLeftAlone()
    {
        const string Fragment = "<html><body>no head here</body></html>";

        Assert.Equal(Fragment, DrawerLogoPatch.Inject(Fragment, LandingPage));
    }

    /// <summary>
    /// The one link every rule aims at, read out of the stylesheet. A rule left with
    /// another link would put the mark and the hidden icon out of step.
    /// </summary>
    private static LinkSelector RowLink(string patched)
    {
        var links = Regex.Matches(patched, @"a\[href(?<op>[\^$*]?=)""(?<value>[^""]*)""\]")
            .Select(m => new LinkSelector(m.Groups["op"].Value, m.Groups["value"].Value))
            .Distinct()
            .ToList();

        return Assert.Single(links);
    }

    /// <summary>
    /// An attribute selector on a link, matched the way CSS matches it.
    /// </summary>
    private sealed record LinkSelector(string Operator, string Value)
    {
        /// <summary>
        /// Whether the selector takes a link with this <c>href</c>.
        /// </summary>
        public bool Matches(string href) => Operator switch
        {
            "=" => href == Value,
            "^=" => href.StartsWith(Value, StringComparison.Ordinal),
            "$=" => href.EndsWith(Value, StringComparison.Ordinal),
            "*=" => href.Contains(Value, StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"No CSS operator {Operator}"),
        };
    }
}
