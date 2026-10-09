using Jellyfin.Plugin.Streamyfin.Injection;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The stylesheet that puts the plugin's logo on its drawer row.
///
/// The dashboard renders a plugin's icon through MUI's icon font component, so
/// <c>MenuIcon</c> can only be a Material ligature and an image has to come from
/// outside the plugin's own pages. File Transformation is that outside.
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
        Assert.Contains(".MuiIcon-root", patched, System.StringComparison.Ordinal);
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
}
