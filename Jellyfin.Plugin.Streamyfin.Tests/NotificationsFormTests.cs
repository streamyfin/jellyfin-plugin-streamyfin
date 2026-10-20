using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The notification events, described from the properties that hold them rather than
/// written a second time as markup in the page.
/// </summary>
public class NotificationsFormTests
{
    /// <summary>
    /// Every event declared on the configuration is described, under the name an
    /// administrator reads.
    /// </summary>
    [Fact]
    public void EveryDeclaredEventIsDescribed()
    {
        var categories = NotificationsForm.Describe()
            .Select(field => field.Category)
            .Distinct()
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Session started", "Playback started", "User locked out", "Item added",
                "Awaited title", "Scheduled task failed", "Plugin changed", "Failed sign in"
            },
            categories);
    }

    /// <summary>
    /// A field's key is the one the payload and the YAML use, so the page sends back
    /// what the server reads.
    /// </summary>
    [Fact]
    public void AFieldIsKeyedTheWayThePayloadIs()
    {
        var keys = NotificationsForm.Describe().Select(field => field.Key).ToArray();

        Assert.Contains("itemAdded.enabled", keys);
        Assert.Contains("sessionStarted.recentEventThreshold", keys);
        Assert.Contains("itemAdded.enabledLibraries", keys);
        Assert.Contains("sessionStarted.enabled", keys);
    }

    /// <summary>
    /// The control comes from the type, the same way it does for a setting.
    /// </summary>
    [Theory]
    [InlineData("itemAdded.enabled", SettingsControl.Toggle)]
    [InlineData("sessionStarted.recentEventThreshold", SettingsControl.Number)]
    [InlineData("itemAdded.enabledLibraries", SettingsControl.List)]
    public void TheControlComesFromTheType(string key, SettingsControl expected)
    {
        Assert.Equal(expected, Field(key).Control);
    }

    /// <summary>
    /// The libraries this server has are the choices for the field that restricts an
    /// event to some of them, so the page draws boxes rather than knowing which field
    /// means libraries.
    /// </summary>
    [Fact]
    public void TheLibrariesAreOfferedAsChoices()
    {
        var libraries = new[]
        {
            new SettingsChoice("a-library-id", "Films"),
            new SettingsChoice("another-library-id", "Series")
        };

        var field = NotificationsForm.Describe(libraries).Single(f => f.Key == "itemAdded.enabledLibraries");

        Assert.Equal(new[] { "Films", "Series" }, field.Options.Select(option => option.Label).ToArray());
    }

    /// <summary>
    /// Without a server to ask, the field is still described, just without choices.
    /// </summary>
    [Fact]
    public void WithoutLibrariesTheFieldIsStillDescribed()
    {
        Assert.Empty(Field("itemAdded.enabledLibraries").Options);
    }

    /// <summary>
    /// Everything in an event only matters while the event is on, which is the same
    /// dependency the settings form already draws.
    /// </summary>
    [Fact]
    public void EveryFieldOfAnEventDependsOnItBeingOn()
    {
        Assert.Null(Field("itemAdded.enabled").DependsOn);
        Assert.Equal("sessionStarted.enabled", Field("sessionStarted.recentEventThreshold").DependsOn);
        Assert.Equal("itemAdded.enabledLibraries", Field("itemAdded.enabledLibraries").Key);
        Assert.Equal("itemAdded.enabled", Field("itemAdded.enabledLibraries").DependsOn);
    }

    /// <summary>
    /// An event is on or off for the server. The three states are a settings idea, and
    /// offering them here would draw a control that writes nowhere.
    /// </summary>
    [Fact]
    public void NoEventFieldIsLockable()
    {
        Assert.All(NotificationsForm.Describe(), field => Assert.False(field.Lockable));
    }

    /// <summary>
    /// The wait is offered on the events that keep two of the same apart, and only there:
    /// a new item, an awaited title and a locked account never read it, so a wait set on
    /// them changed nothing.
    /// </summary>
    [Fact]
    public void AWaitIsOfferedOnlyWhereTheEventWaits()
    {
        var waits = NotificationsForm.Describe()
            .Select(field => field.Key)
            .Where(key => key.EndsWith(".recentEventThreshold", System.StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "sessionStarted.recentEventThreshold", "playbackStarted.recentEventThreshold",
                "taskFailed.recentEventThreshold", "pluginChanged.recentEventThreshold",
                "signInFailed.recentEventThreshold"
            },
            waits);
    }

    /// <summary>
    /// A wait left empty is the event's own, and the field says which rather than leaving
    /// the administrator to guess: five seconds, and five minutes for a refused sign in.
    /// </summary>
    [Theory]
    [InlineData("sessionStarted", 5)]
    [InlineData("playbackStarted", 5)]
    [InlineData("taskFailed", 5)]
    [InlineData("pluginChanged", 5)]
    [InlineData("signInFailed", 300)]
    public void AWaitSaysTheEventsOwn(string eventKey, double seconds)
    {
        Assert.Equal(seconds, Field($"{eventKey}.recentEventThreshold").ServerDefault);
    }

    /// <summary>
    /// The events the Targeting tab lists say the same, so a level is offered a wait only
    /// where it changes something.
    /// </summary>
    [Fact]
    public void TheEventsSayWhichOnesWaitAndHowLong()
    {
        var waits = NotificationsForm.Events().ToDictionary(one => one.Key, one => one.Wait);

        Assert.Equal(5, waits["sessionStarted"]);
        Assert.Equal(5, waits["playbackStarted"]);
        Assert.Null(waits["userLockedOut"]);
        Assert.Null(waits["itemAdded"]);
        Assert.Null(waits["awaitedTitle"]);
        Assert.Equal(5, waits["taskFailed"]);
        Assert.Equal(5, waits["pluginChanged"]);
        Assert.Equal(300, waits["signInFailed"]);
    }

    /// <summary>
    /// A wait in seconds cannot be negative, and the page refuses one before the server
    /// has to.
    /// </summary>
    [Fact]
    public void AWaitCannotBeNegative()
    {
        Assert.Equal(0, Field("sessionStarted.recentEventThreshold").Minimum);
    }

    /// <summary>
    /// The sentence explaining an event sits on the switch that turns it on, since a
    /// card header is not somewhere an administrator can act.
    /// </summary>
    [Fact]
    public void TheEventsOwnSentenceSitsOnItsSwitch()
    {
        Assert.Contains("movies or episodes", Field("itemAdded.enabled").Description, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The switch that turns an event on is the first thing in its card. Reflection
    /// hands a derived type's own properties back first, so the libraries an event may
    /// come from used to be drawn above the switch.
    /// </summary>
    [Fact]
    public void AnEventLeadsWithTheSwitchThatTurnsItOn()
    {
        var itemAdded = NotificationsForm.Describe()
            .Where(field => field.Category == "Item added")
            .Select(field => field.Key)
            .ToArray();

        Assert.Equal(
            new[] { "itemAdded.enabled", "itemAdded.enabledLibraries" },
            itemAdded);
    }

    private static SettingsFormField Field(string key) =>
        NotificationsForm.Describe().Single(field => field.Key == key);

    /// <summary>
    /// The awaited titles are an event the server declares, so the pages list it, and it
    /// names nobody but the person told (#225).
    /// </summary>
    [Fact]
    public void TheAwaitedTitlesAreAnEventTheServerDeclares() =>
        Assert.Contains(
            NotificationsForm.Events(),
            declared => declared.Key == NotificationEvents.AwaitedTitle && !declared.AboutSomebodyElse);

    /// <summary>
    /// A new server has them on.
    /// </summary>
    [Fact]
    public void ANewServerHasTheAwaitedTitlesOn() =>
        Assert.True(Configuration.PluginConfiguration.DefaultConfig().notifications?.AwaitedTitle?.Enabled);
}
