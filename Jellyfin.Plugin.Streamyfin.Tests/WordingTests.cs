using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Streamyfin;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Writing a notification differently from the way the plugin writes it (#34).
/// </summary>
/// <remarks>
/// The request is for the wording, not for a templating engine: the issue asks to change
/// the title and the body, and the pushback on it is that this plugin has far less
/// metadata to interpolate than the webhook plugin, since it waits and consolidates. So
/// what is offered is the sentence, with the same placeholders the sentence already has,
/// per language.
/// </remarks>
public class WordingTests
{
    private static List<WordingOverride> Said(params (string Key, string? Locale, string Text)[] said)
    {
        var list = new List<WordingOverride>();

        foreach (var (key, locale, text) in said)
        {
            list.Add(new WordingOverride { Key = key, Locale = locale, Text = text });
        }

        return list;
    }

    /// <summary>
    /// Nothing said leaves the plugin's own wording standing.
    /// </summary>
    [Fact]
    public void WithNothingSaidTheResourceStands()
    {
        Assert.Null(Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-FR"), null));
        Assert.Null(Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-FR"), []));
        Assert.Null(Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-FR"), Said(("SessionStartTitle", null, "Hello"))));
    }

    /// <summary>
    /// An override with no language is the wording in every language.
    /// </summary>
    [Fact]
    public void AnOverrideWithoutALanguageIsForEveryLanguage()
    {
        var said = Said(("TaskFailedTitle", null, "Something broke"));

        Assert.Equal("Something broke", Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-FR"), said));
        Assert.Equal("Something broke", Wording.For("TaskFailedTitle", CultureInfo.InvariantCulture, said));
    }

    /// <summary>
    /// The most specific language wins: the exact tag, then the language it belongs to,
    /// then the one that named no language at all.
    /// </summary>
    [Fact]
    public void TheMostSpecificLanguageWins()
    {
        var said = Said(
            ("TaskFailedTitle", null, "every language"),
            ("TaskFailedTitle", "fr", "en français"),
            ("TaskFailedTitle", "fr-CA", "au Québec"));

        Assert.Equal("au Québec", Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-CA"), said));
        Assert.Equal("en français", Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-FR"), said));
        Assert.Equal("en français", Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr"), said));
        Assert.Equal("every language", Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("nl-NL"), said));
    }

    /// <summary>
    /// A language is written by hand, so it is matched the way a language is: ignoring
    /// case, and reading an underscore as the separator some clients send.
    /// </summary>
    [Fact]
    public void ALanguageIsMatchedTheWayALanguageIs()
    {
        var said = Said(("TaskFailedTitle", "FR_ca", "au Québec"));

        Assert.Equal("au Québec", Wording.For("TaskFailedTitle", CultureInfo.GetCultureInfo("fr-CA"), said));
    }

    /// <summary>
    /// A blank override is not an override: emptying the box on the page puts the
    /// plugin's own wording back rather than sending an empty notification.
    /// </summary>
    [Fact]
    public void ABlankOverrideIsNotAnOverride()
    {
        Assert.Null(Wording.For("TaskFailedTitle", CultureInfo.InvariantCulture, Said(("TaskFailedTitle", null, "   "))));
    }

    /// <summary>
    /// The whole point: what comes out is what was asked for, placeholders and all.
    /// </summary>
    [Fact]
    public void TheHelperUsesTheWordingItIsGiven()
    {
        var helper = new LocalizationHelper(null, null, () => Said(
            ("TaskFailedTitle", null, "Something broke"),
            ("TaskFailedWithReason", "fr", "{0} a cassé : {1}")));

        Assert.Equal("Something broke", helper.GetString("TaskFailedTitle", CultureInfo.GetCultureInfo("nl-NL")));
        Assert.Equal(
            "Clean Activity Log a cassé : disk full",
            helper.GetFormatted("TaskFailedWithReason", CultureInfo.GetCultureInfo("fr-FR"), "Clean Activity Log", "disk full"));
    }

    /// <summary>
    /// A sentence asking for a placeholder the event does not have would throw inside an
    /// event handler the server is waiting on. The plugin's own wording answers instead.
    /// </summary>
    [Fact]
    public void AWordingThatAsksForWhatIsNotThereFallsBack()
    {
        var helper = new LocalizationHelper(null, null, () => Said(("TaskFailed", null, "{0} failed, and {7} too")));

        Assert.Equal(
            "Clean Activity Log failed",
            helper.GetFormatted("TaskFailed", CultureInfo.InvariantCulture, "Clean Activity Log"));
    }

    /// <summary>
    /// A key this server does not have is refused rather than stored, the same way an
    /// unknown event is: it would be read on every send and silently skipped.
    /// </summary>
    [Fact]
    public void AKeyThatDoesNotExistIsRefused()
    {
        var problem = NotificationsValidation.CheckWording(Said(("TaskFaild", null, "typo")));

        Assert.NotNull(problem);
        Assert.Contains("TaskFaild", problem, System.StringComparison.Ordinal);

        Assert.Null(NotificationsValidation.CheckWording(Said(("TaskFailedTitle", null, "fine"))));
        Assert.Null(NotificationsValidation.CheckWording(null));
    }

    /// <summary>
    /// And so is a sentence that asks for a placeholder the sentence it replaces does not
    /// have, since that is the one mistake that would otherwise only show when the event
    /// fires.
    /// </summary>
    [Fact]
    public void AWordingAskingForAPlaceholderThatIsNotThereIsRefused()
    {
        Assert.NotNull(NotificationsValidation.CheckWording(Said(("TaskFailedTitle", null, "{0} broke"))));
        Assert.Null(NotificationsValidation.CheckWording(Said(("TaskFailedWithReason", null, "{0}: {1}"))));
        Assert.Null(NotificationsValidation.CheckWording(Said(("TaskFailedWithReason", null, "{0} broke"))));
    }

    /// <summary>
    /// The helper is built without a wording source everywhere the plugin builds one, and
    /// reads the plugin's configuration then. With no plugin loaded, which is every test
    /// and the moments around a restart, it answers the resource rather than throwing
    /// inside an event handler.
    /// </summary>
    [Fact]
    public void WithNoPluginLoadedItIsStillTheResource()
    {
        var helper = new LocalizationHelper(null, null);

        Assert.Equal("Scheduled task failed", helper.GetString("TaskFailedTitle", CultureInfo.InvariantCulture));
        Assert.Equal(
            "Clean Activity Log failed: disk full",
            helper.GetFormatted("TaskFailedWithReason", CultureInfo.InvariantCulture, "Clean Activity Log", "disk full"));
    }

    /// <summary>
    /// The whole notifications block is checked the same way, since that is the door every
    /// writer comes through: the YAML tab, the admin page and the backup restore.
    /// </summary>
    [Fact]
    public void TheWholeBlockIsChecked()
    {
        var notifications = new Notifications
        {
            Wording = [new WordingOverride { Key = "TaskFailedTitle", Text = "{0} broke" }]
        };

        Assert.NotNull(NotificationsValidation.Check(notifications));
        Assert.Contains("TaskFailedTitle", NotificationsValidation.Check(notifications)!, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A placeholder number too big to be one is refused like any other wording that asks
    /// for what is not there, rather than throwing out of the save.
    /// </summary>
    [Fact]
    public void APlaceholderTooBigToBeOneIsRefused()
    {
        var said = Said(("TaskFailedWithReason", null, "{99999999999} failed"));

        Assert.NotNull(NotificationsValidation.CheckWording(said));
        Assert.Equal(0, Wording.Asks("{99999999999}"));
    }

    /// <summary>
    /// What the page lists: every sentence the plugin can write, with what it says today
    /// and how many things it names.
    /// </summary>
    [Fact]
    public void TheSentencesAreDescribedForThePage()
    {
        var sentences = Wording.Sentences();

        var failed = Assert.Single(sentences, one => one.Key == "TaskFailedWithReason");

        Assert.Equal("{0} failed: {1}", failed.Text);
        Assert.Equal(2, failed.Placeholders);

        Assert.Contains(sentences, one => one.Key == "TaskFailedTitle" && one.Placeholders == 0);
        Assert.True(sentences.Count > 40);
    }
}
