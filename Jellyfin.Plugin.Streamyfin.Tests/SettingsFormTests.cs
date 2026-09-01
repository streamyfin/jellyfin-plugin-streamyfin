using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The description of the admin form, which is what the page renders from.
/// </summary>
/// <remarks>
/// P3.1 generated the form from the JSON schema, and the schema had to be reshaped
/// four separate ways before json-editor would draw it: a single branch
/// <c>oneOf</c> unwrapped, a nullable enum collapsed, secrets inlined as password
/// fields, shared descriptions blanked. Five of the seven tests on that schema
/// pinned those workarounds rather than anything an administrator cares about.
///
/// <para>
/// This says the same thing directly instead. Every setting names the control it
/// needs, and it names it in C#, where a test can hold it to account, rather than
/// leaving the page to work it out from <c>$ref</c> chains at runtime where nothing
/// can. The list is still nobody's to maintain: it is
/// <see cref="SettingsSchema.Descriptors"/>, read by reflection.
/// </para>
/// </remarks>
public class SettingsFormTests
{
    private static SettingsFormField Field(string key) =>
        Assert.Single(SettingsForm.Describe().Where(f => f.Key == key));

    /// <summary>
    /// Every declared setting reaches the form. A setting the form cannot draw is a
    /// setting an administrator cannot reach, which is the failure P3.1 existed to
    /// fix and would be reintroduced silently by a value type nothing maps.
    /// </summary>
    [Fact]
    public void EveryDeclaredSettingHasAControl()
    {
        var fields = SettingsForm.Describe();

        Assert.Equal(SettingsSchema.Descriptors.Count, fields.Count);
        Assert.Empty(fields.Where(f => f.Control == SettingsControl.Unknown));
    }

    /// <summary>
    /// The control follows from the value's type, decided once here rather than in
    /// each page that renders a setting.
    /// </summary>
    [Theory]
    [InlineData("showHomeTitles", SettingsControl.Toggle)]
    [InlineData("forwardSkipTime", SettingsControl.Number)]
    [InlineData("jellyseerrServerUrl", SettingsControl.Text)]
    [InlineData("jellyseerrApiKey", SettingsControl.Secret)]
    [InlineData("openSubtitlesApiKey", SettingsControl.Secret)]
    [InlineData("videoPlayer", SettingsControl.Select)]
    [InlineData("defaultBitrate", SettingsControl.Select)]
    [InlineData("hiddenLibraries", SettingsControl.List)]
    [InlineData("defaultAudioLanguage", SettingsControl.Language)]
    [InlineData("home", SettingsControl.Composite)]
    public void TheControlFollowsTheValueType(string key, SettingsControl expected)
    {
        Assert.Equal(expected, Field(key).Control);
    }

    /// <summary>
    /// A dropdown arrives with its choices, so the page holds no list of its own.
    /// </summary>
    [Fact]
    public void ASelectCarriesItsChoices()
    {
        var options = Field("videoPlayer").Options;

        Assert.NotEmpty(options);
        Assert.Empty(options.Where(o => string.IsNullOrWhiteSpace(o.Value)));
        Assert.Empty(options.Where(o => string.IsNullOrWhiteSpace(o.Label)));
    }

    /// <summary>
    /// A choice is labelled for a person, not for the compiler.
    /// </summary>
    /// <remarks>
    /// Nothing in <c>Enums.cs</c> carries a display name, so before this an
    /// administrator picking a playback quality read <c>_250KB</c>, and one choosing
    /// a subtitle mode read <c>OnlyForced</c>. The label is derived from the member
    /// name, and a <c>Display</c> attribute overrides it where deriving would be
    /// wrong.
    /// </remarks>
    [Theory]
    [InlineData("defaultBitrate", "_250KB", "250 KB")]
    [InlineData("subtitleMode", "OnlyForced", "Only forced")]
    public void AChoiceIsLabelledForAPerson(string key, string value, string expected)
    {
        var option = Assert.Single(Field(key).Options.Where(o => o.Value == value));

        Assert.Equal(expected, option.Label);
    }

    /// <summary>
    /// The playback quality keeps its "no cap" choice, which the app reads as null.
    /// </summary>
    [Fact]
    public void ThePlaybackQualityOffersNoCap()
    {
        var options = Field("defaultBitrate").Options;

        Assert.Equal("Max", options[0].Label);
        Assert.Null(options[0].Value);
    }

    /// <summary>
    /// A credential says so, so a page never has to carry a list of which keys are
    /// secret in order to mask them.
    /// </summary>
    [Fact]
    public void ACredentialSaysSo()
    {
        var secrets = SettingsForm.Describe().Where(f => f.Control == SettingsControl.Secret);

        Assert.Equal(
            SettingsSchema.Secrets.Select(d => d.Key).OrderBy(k => k),
            secrets.Select(f => f.Key).OrderBy(k => k));
    }

    /// <summary>
    /// A number carries the bounds its setting declares.
    /// </summary>
    /// <remarks>
    /// The hand written page had <c>min="0" max="60" step="5"</c> on the skip times
    /// and <c>max="120"</c> on the subtitle size. Generating the form from the schema
    /// dropped all three, since nothing in C# recorded them, and a skip time has been
    /// unbounded ever since. They live on the property now, where both the form and a
    /// future validator can read them.
    /// </remarks>
    [Theory]
    [InlineData("forwardSkipTime", 0, 60, 5)]
    [InlineData("rewindSkipTime", 0, 60, 5)]
    [InlineData("subtitleSize", 0, 120, 5)]
    public void ANumberCarriesItsBounds(string key, double min, double max, double step)
    {
        var field = Field(key);

        Assert.Equal(min, field.Minimum);
        Assert.Equal(max, field.Maximum);
        Assert.Equal(step, field.Step);
    }

    /// <summary>
    /// A setting with no declared bounds says nothing rather than inventing some.
    /// </summary>
    [Fact]
    public void AnUnboundedNumberClaimsNoBounds()
    {
        var field = Field("maxAutoPlayEpisodeCount");

        Assert.Null(field.Minimum);
        Assert.Null(field.Maximum);
    }

    /// <summary>
    /// The section, the label and the help text come from the setting itself, in the
    /// order the settings are declared.
    /// </summary>
    [Fact]
    public void TheFieldCarriesWhatTheSettingDeclares()
    {
        var fields = SettingsForm.Describe();
        var field = Field("showHomeBackdrop");

        Assert.Equal("Home and appearance", field.Category);
        Assert.Equal("Show the home backdrop", field.Title);
        Assert.Contains("backdrop", field.Description, System.StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            SettingsSchema.Descriptors.Select(d => d.Key),
            fields.Select(f => f.Key));
    }

    /// <summary>
    /// Whether an administrator can pin a setting travels with it, since that is a
    /// second control beside the value and the page has to know to draw it.
    /// </summary>
    [Fact]
    public void TheLockTravelsWithTheSetting()
    {
        var fields = SettingsForm.Describe();

        Assert.Equal(
            SettingsSchema.Descriptors.Where(d => d.IsLockable).Select(d => d.Key),
            fields.Where(f => f.Lockable).Select(f => f.Key));
    }
}
