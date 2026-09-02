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

    /// <summary>
    /// A choice is spelled the way the store writes it, so a stored value always matches
    /// one of the dropdown's options.
    /// </summary>
    /// <remarks>
    /// Several enums carry an <c>EnumMember</c> value that differs from the member name:
    /// <c>Allow51</c> is stored as <c>5.1</c>, <c>GpuNext</c> as <c>gpu-next</c>,
    /// <c>Default</c> as <c>default</c>. A dropdown offering the member name would send
    /// a spelling the YAML reader rejects, and would never show the stored value as
    /// selected. The round trip pins both: what a choice sends is accepted, and it comes
    /// back written the same way.
    /// </remarks>
    [Fact]
    public void AChoiceIsSpelledTheWayTheStoreWritesIt()
    {
        var serialization = new SerializationHelper();

        foreach (var field in SettingsForm.Describe().Where(f => f.Control == SettingsControl.Select))
        {
            foreach (var option in field.Options.Where(o => o.Value is not null))
            {
                var yaml = $"settings:\n  {field.Key}:\n    locked: false\n    value: {option.Value}\n";

                var config = serialization.Deserialize<Configuration.Config>(yaml);
                var written = serialization.SerializeToYaml(config);
                var stored = System.Text.RegularExpressions.Regex.Match(written, @"value: ['""]?([^'""\r\n]+)").Groups[1].Value;

                Assert.True(
                    option.Value == stored,
                    $"{field.Key}: the form offers '{option.Value}' and the store writes '{stored}'");
            }
        }
    }

    /// <summary>
    /// A setting that only matters while another one is on says which one.
    /// </summary>
    /// <remarks>
    /// Each pair was read in the app rather than guessed from the names: the restart
    /// flag is passed to the mute hook under <c>enabled: subtitlesOnMute</c>, the
    /// look-ahead count is read after an early return on <c>audioLookaheadEnabled</c>,
    /// the hold rate after one on <c>enableHoldToSpeed</c>, and the background opacity
    /// only feeds the alpha of a background that <c>subtitleBackground</c> draws.
    /// </remarks>
    [Theory]
    [InlineData("subtitlesOnMuteAllowRestart", "subtitlesOnMute")]
    [InlineData("audioLookaheadCount", "audioLookaheadEnabled")]
    [InlineData("holdToSpeedRate", "enableHoldToSpeed")]
    [InlineData("subtitleBackgroundOpacity", "subtitleBackground")]
    public void ADependentSettingNamesWhatItDependsOn(string key, string parent)
    {
        Assert.Equal(parent, Field(key).DependsOn);
    }

    /// <summary>
    /// A setting nothing gates depends on nothing, so the form does not grey it.
    /// </summary>
    [Fact]
    public void AnIndependentSettingDependsOnNothing()
    {
        Assert.Null(Field("forwardSkipTime").DependsOn);
    }

    /// <summary>
    /// A dependency names a declared toggle. The form greys the dependent setting when
    /// that toggle is locked off, which it can only do for a setting that exists and
    /// is a toggle.
    /// </summary>
    [Fact]
    public void ADependencyPointsAtADeclaredToggle()
    {
        var fields = SettingsForm.Describe();

        foreach (var field in fields.Where(f => f.DependsOn is not null))
        {
            var parent = Assert.Single(fields.Where(f => f.Key == field.DependsOn));

            Assert.Equal(SettingsControl.Toggle, parent.Control);
            Assert.NotEqual(field.Key, parent.Key);
        }
    }

    /// <summary>
    /// A field travels with its control named, not numbered, whatever the host's JSON
    /// options. The page switches on the name.
    /// </summary>
    [Fact]
    public void TheControlTravelsByName()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Field("showHomeTitles"));

        Assert.Contains("\"control\":\"Toggle\"", json);
        Assert.Contains("\"dependsOn\":null", json);
    }

    /// <summary>
    /// A number says whether it takes whole numbers only. Most numeric settings are
    /// <c>Lockable&lt;int&gt;</c>, and a form that accepts 2.5 for one hands the server a
    /// value it refuses with a message that points at no field.
    /// </summary>
    [Theory]
    [InlineData("forwardSkipTime", true)]
    [InlineData("subtitleSize", true)]
    [InlineData("defaultPlaybackSpeed", false)]
    [InlineData("holdToSpeedRate", false)]
    public void ANumberSaysWhetherItIsWhole(string key, bool integer)
    {
        Assert.Equal(integer, Field(key).Integer);
    }

    /// <summary>
    /// Only a number claims to be whole; the flag means nothing on any other control.
    /// </summary>
    [Fact]
    public void OnlyANumberIsWhole()
    {
        Assert.Empty(SettingsForm.Describe().Where(f => f.Integer && f.Control != SettingsControl.Number));
    }
}
