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
    private const string Document = "<html><head><title>Jellyfin</title></head><body></body></html>";

    /// <summary>
    /// The stylesheet lands inside the head, before it closes.
    /// </summary>
    [Fact]
    public void TheStyleGoesInsideTheHead()
    {
        var patched = DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = Document });

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
        var once = DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = Document });
        var twice = DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = once });

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// The rule points at the image Jellyfin already serves for the plugin, by id and by
    /// version. The id alone answers 405, so the version is not optional.
    /// </summary>
    [Fact]
    public void TheRulePointsAtThePluginsOwnImage()
    {
        var patched = DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = Document });

        var version = typeof(StreamyfinPlugin).Assembly.GetName().Version!.ToString();

        Assert.Contains($"/Plugins/1e9e5d386e6746158719e98a5c34f004/{version}/Image", patched, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule targets the drawer row for the landing page, which is the row that asks
    /// for a menu entry.
    /// </summary>
    [Fact]
    public void TheRuleTargetsTheDrawerRow()
    {
        var patched = DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = Document });

        Assert.Contains("configurationpage?name=Application", patched, System.StringComparison.Ordinal);
        Assert.Contains(".MuiIcon-root", patched, System.StringComparison.Ordinal);
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
        Assert.Equal(string.Empty, DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = contents }));
    }

    /// <summary>
    /// A document with no head is served unchanged rather than mangled.
    /// </summary>
    [Fact]
    public void ADocumentWithNoHeadIsLeftAlone()
    {
        const string Fragment = "<html><body>no head here</body></html>";

        Assert.Equal(Fragment, DrawerLogoPatch.IndexHtml(new FileTransformationPayload { Contents = Fragment }));
    }
}
