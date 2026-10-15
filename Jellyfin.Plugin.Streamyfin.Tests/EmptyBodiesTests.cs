using System.Linq;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Bodies with nothing in them, which two routes for administrators answered with a 500.
/// </summary>
/// <remarks>
/// Found by fuzzing the plugin's routes from Jellyfin's OpenAPI document on 10.11.11 and
/// 12.2.0, before the 0.70.0.0 release.
/// </remarks>
public class EmptyBodiesTests
{
    private readonly SerializationHelper _serialization = new();

    /// <summary>
    /// What saving a Yaml document answers, through the route's own sequence.
    /// </summary>
    private string? Decide(string yaml) =>
        StreamyfinController.ToSave(_serialization, yaml, new PluginConfiguration().Config.settings).Problem;

    /// <summary>
    /// A Yaml document that sets nothing is refused with a sentence, and nothing is saved.
    /// </summary>
    /// <remarks>
    /// The deserializer reads a document with nothing in it as null. Saving then read that
    /// null's settings and answered 500; before 0.70 the same document replaced the whole
    /// configuration with nothing. A document of empty sections, <c>{}</c> among them,
    /// reads as a configuration with no value anywhere, and saving it replaced the whole
    /// configuration with nothing while answering that it went well. So did a document whose
    /// only value was a blank address, which the checks tidy away before saving.
    /// </remarks>
    /// <param name="yaml">What the editor sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   \n\t\n")]
    [InlineData("# only a comment\n")]
    [InlineData("---\n")]
    [InlineData("{}")]
    [InlineData("settings:\n")]
    [InlineData("settings: ~\n")]
    [InlineData("notifications:\n")]
    [InlineData("other:\n")]
    [InlineData("settings: {}\n")]
    [InlineData("notifications: {}\n")]
    [InlineData("other: {}\n")]
    [InlineData("other:\n  homePage: ~\n")]
    [InlineData("settings:\n  forwardSkipTime: ~\n")]
    [InlineData("settings:\n  jellyseerrServerUrl:\n    value: \"   \"\n")]
    [InlineData("settings:\n  seerr:\n    serverUrl:\n      value: \"  \"\n")]
    [InlineData("settings:\n  seerr:\n    serverUrl: {}\n")]
    [InlineData("settings:\n  forwardSkipTime: {}\n")]
    [InlineData("settings:\n  jellyseerrServerUrl:\n    value:\n")]
    [InlineData("settings:\n  jellyseerrServerUrl:\n    value: ~\n")]
    [InlineData("notifications:\n  itemAdded: {}\n")]
    [InlineData("settings:\n  forwardSkipTime: &none\n    value: ~\n  rewindSkipTime: *none\n")]
    public void AYamlThatSetsNothingIsRefused(string yaml)
    {
        Assert.Equal(StreamyfinController.EmptyYaml, Decide(yaml));
    }

    /// <summary>
    /// A document that sets one thing is saved, even next to sections with nothing in them.
    /// An empty list, a 0 and a false are values somebody wrote.
    /// </summary>
    /// <param name="yaml">What the editor sent.</param>
    [Theory]
    [InlineData("settings:\n  forwardSkipTime:\n    value: 20\n")]
    [InlineData("settings: {}\nother:\n  homePage: /web\n")]
    [InlineData("settings:\n  hiddenLibraries:\n    value: []\n")]
    [InlineData("notifications:\n  wording: []\n")]
    [InlineData("settings:\n  forwardSkipTime:\n    value: 0\n")]
    [InlineData("settings:\n  autoLoginJellyseerr:\n    value: false\n")]
    [InlineData("settings:\n  forwardSkipTime: &skip\n    value: 20\n  rewindSkipTime: *skip\n")]
    public void AYamlThatSetsSomethingIsSaved(string yaml)
    {
        Assert.Null(Decide(yaml));
    }

    /// <summary>
    /// A null tagged as a string writes no value either: the deserializer reads it as null,
    /// so counting it as one would store a configuration with nothing in it. A quoted null is
    /// text, and is kept.
    /// </summary>
    /// <param name="written">How the document writes the value.</param>
    /// <param name="read">What the setting holds once read.</param>
    [Theory]
    [InlineData("!!str null", null)]
    [InlineData("!!str ~", null)]
    [InlineData("\"null\"", "null")]
    public void AValueIsJudgedAsTheDeserializerReadsIt(string written, string? read)
    {
        var yaml = $"settings:\n  jellyseerrApiKey:\n    value: {written}\n";

        Assert.Equal(read, _serialization.Deserialize<Config>(yaml)?.settings?.jellyseerrApiKey?.value);
        Assert.Equal(read is null ? StreamyfinController.EmptyYaml : null, Decide(yaml));
    }

    /// <summary>
    /// A number that is not one, or an infinity, is refused with the setting's name rather
    /// than failing the save.
    /// </summary>
    /// <remarks>
    /// Yaml reads <c>.nan</c> and <c>.inf</c> as numbers, and a comparison with NaN is
    /// never true, so the bounds let it through. The Json the store writes has no spelling
    /// for either, and the save answered 500.
    /// </remarks>
    /// <param name="yaml">What the editor sent.</param>
    [Theory]
    [InlineData("settings:\n  defaultPlaybackSpeed:\n    value: .nan\n")]
    [InlineData("settings:\n  defaultPlaybackSpeed:\n    value: -.inf\n")]
    public void ANumberThatIsNotOneIsRefused(string yaml)
    {
        var problem = Decide(yaml);

        Assert.NotNull(problem);
        Assert.Contains("needs a number", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Every setting that holds a fractional number refuses one that is not a number, with
    /// or without bounds of its own.
    /// </summary>
    [Fact]
    public void EveryFractionalSettingRefusesNaN()
    {
        var fractional = Configuration.Settings.SettingsSchema.Descriptors
            .Where(descriptor => descriptor.ValueType == typeof(double) || descriptor.ValueType == typeof(float))
            .ToArray();
        Assert.NotEmpty(fractional);

        foreach (var descriptor in fractional)
        {
            var settings = new Configuration.Settings.Settings();
            descriptor.Property.SetValue(settings, System.Activator.CreateInstance(descriptor.Property.PropertyType));
            descriptor.Write(settings, descriptor.ValueType == typeof(float) ? float.NaN : double.NaN);

            var problem = Configuration.Settings.SettingsValidation.Check(settings);

            Assert.True(problem?.Contains("needs a number", System.StringComparison.Ordinal) == true, $"{descriptor.Key}: {problem ?? "accepted"}");
        }
    }

    /// <summary>
    /// A Yaml document with something in it is read, and nothing is said about it.
    /// </summary>
    [Fact]
    public void AYamlWithSettingsIsRead()
    {
        var (config, problem) = StreamyfinController.ReadYaml(_serialization, "settings:\n  forwardSkipTime:\n    value: 20\n");

        Assert.Null(problem);
        Assert.Equal(20, config?.settings?.forwardSkipTime?.value);
    }

    /// <summary>
    /// A Yaml document the reader cannot make sense of is refused with what the reader said,
    /// not with the sentence for an empty one.
    /// </summary>
    [Fact]
    public void AnUnreadableYamlIsRefusedWithTheReadersReason()
    {
        var (config, problem) = StreamyfinController.ReadYaml(_serialization, "settings: [unclosed\n");

        Assert.Null(config);
        Assert.NotNull(problem);
        Assert.NotEqual(StreamyfinController.EmptyYaml, problem);
    }

    /// <summary>
    /// An empty item of a posted list of notifications is left out, as one with nothing to
    /// show always was, instead of failing the whole request.
    /// </summary>
    [Fact]
    public void AnEmptyItemOfAPostedListIsNotSent()
    {
        Assert.False(StreamyfinController.IsSendable(null));
    }

    /// <summary>
    /// The rule for the other items is unchanged: a body with a word in it is needed, and a
    /// title may be left out, but one that is there needs a word too.
    /// </summary>
    /// <param name="title">The item's title.</param>
    /// <param name="body">The item's body.</param>
    /// <param name="sent">Whether it is sent.</param>
    [Theory]
    [InlineData("Title", "Body", true)]
    [InlineData(null, "Body", true)]
    [InlineData("", "Body", true)]
    [InlineData("🎬 Dune", "Dune is now available", true)]
    [InlineData("Title", null, false)]
    [InlineData("Title", "   ", false)]
    [InlineData(null, null, false)]
    [InlineData("   ", "Body", false)]
    [InlineData("🎬", "Body", false)]
    [InlineData("...", "Body", false)]
    public void AnItemIsSentWhenItsBodyAndAnyTitleHaveAWord(string? title, string? body, bool sent)
    {
        Assert.Equal(sent, StreamyfinController.IsSendable(new Notification { Title = title, Body = body }));
    }
}
