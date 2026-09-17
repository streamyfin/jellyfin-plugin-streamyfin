using System;
using System.Globalization;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What an administrator is told about a server that is not doing what it should: a
/// scheduled task that failed, a plugin that changed, a sign in that was refused.
/// </summary>
/// <remarks>
/// The wording follows Jellyfin's own activity log, in every language it is translated
/// into here, so an administrator reads the same sentence about the same event whether it
/// reaches them through the dashboard or through their phone.
/// </remarks>
public class AdminEventsTests
{
    private static readonly LocalizationHelper Localization = new(null, null);

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    /// <summary>
    /// A failed task is named, with the reason it gave.
    /// </summary>
    [Fact]
    public void AFailedTaskIsNamedWithItsReason()
    {
        var message = AdminEvents.TaskFailed(Localization, "Scan Media Library", "Boom", English);

        Assert.Equal("Scheduled task failed", message.Title);
        Assert.Equal("Scan Media Library failed: Boom", message.Body);
    }

    /// <summary>
    /// A task that failed without saying why is still reported.
    /// </summary>
    [Fact]
    public void AFailedTaskWithoutAReasonIsStillReported()
    {
        Assert.Equal("Scan Media Library failed", AdminEvents.TaskFailed(Localization, "Scan Media Library", null, English).Body);
        Assert.Equal("Scan Media Library failed", AdminEvents.TaskFailed(Localization, "Scan Media Library", "   ", English).Body);
    }

    /// <summary>
    /// A stack trace is not a notification: the first line is kept, and it is cut before a
    /// push that Expo would refuse for its size.
    /// </summary>
    [Fact]
    public void ALongReasonIsCutToItsFirstLine()
    {
        var reason = "Could not write to the folder\n   at System.IO.File.Open()\n   at Jellyfin";

        Assert.Equal("Scan failed: Could not write to the folder", AdminEvents.TaskFailed(Localization, "Scan", reason, English).Body);

        var long_ = new string('x', 400);
        var body = AdminEvents.TaskFailed(Localization, "Scan", long_, English).Body;

        Assert.True(body!.Length < 200, $"body was {body.Length} characters");
        Assert.EndsWith("…", body, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The French of Jellyfin's own activity log, rather than a second wording for the same
    /// event.
    /// </summary>
    [Fact]
    public void ItIsTranslated()
    {
        var message = AdminEvents.TaskFailed(Localization, "Scan", "Boom", French);

        Assert.Equal("Échec de tâche planifiée", message.Title);
        Assert.Equal("Scan a échoué : Boom", message.Body);
    }

    /// <summary>
    /// A plugin that arrives, changes version or goes is named, with the version when
    /// there is one.
    /// </summary>
    [Theory]
    [InlineData(PluginChange.Installed, "1.2.3", "Plugin installed", "Marlin 1.2.3 was installed")]
    [InlineData(PluginChange.Installed, null, "Plugin installed", "Marlin was installed")]
    [InlineData(PluginChange.Updated, "1.2.3", "Plugin updated", "Marlin was updated to 1.2.3")]
    [InlineData(PluginChange.Updated, null, "Plugin updated", "Marlin was updated")]
    [InlineData(PluginChange.Uninstalled, "1.2.3", "Plugin uninstalled", "Marlin 1.2.3 was uninstalled")]
    [InlineData(PluginChange.Uninstalled, null, "Plugin uninstalled", "Marlin was uninstalled")]
    public void APluginChangeIsNamed(PluginChange change, string? version, string title, string body)
    {
        var message = AdminEvents.PluginChanged(Localization, change, "Marlin", version, English);

        Assert.Equal(title, message.Title);
        Assert.Equal(body, message.Body);
    }

    /// <summary>
    /// A refused sign in names what was tried and where it came from.
    /// </summary>
    [Fact]
    public void ARefusedSignInNamesWhatWasTried()
    {
        var message = AdminEvents.SignInFailed(Localization, "bob", "10.0.0.5", English);

        Assert.Equal("Failed sign in", message.Title);
        Assert.Equal("bob could not sign in from 10.0.0.5", message.Body);
    }

    /// <summary>
    /// Without an address, or without a name, the sentence still reads.
    /// </summary>
    [Fact]
    public void ARefusedSignInReadsWithWhateverIsKnown()
    {
        Assert.Equal("bob could not sign in", AdminEvents.SignInFailed(Localization, "bob", null, English).Body);
        Assert.Equal("A sign in from 10.0.0.5 was refused", AdminEvents.SignInFailed(Localization, "  ", "10.0.0.5", English).Body);
        Assert.Equal("A sign in was refused", AdminEvents.SignInFailed(Localization, null, null, English).Body);
    }

    /// <summary>
    /// A version arriving where another one is already installed is an update, whatever
    /// Jellyfin calls the event.
    /// </summary>
    /// <remarks>
    /// Its InstallationManager publishes the updated event only when the version being
    /// installed is the one already there, a repair, so a plugin going from 17 to 19
    /// arrives as an install. The version it replaces stays loaded until the restart,
    /// beside the arriving one the install adds straight away, and that is what tells them
    /// apart.
    /// </remarks>
    [Fact]
    public void AVersionArrivingWhereAnotherIsInstalledIsAnUpdate()
    {
        var id = Guid.NewGuid();

        Assert.Equal(PluginChange.Updated, PluginChangedEvent.InstallOrUpdate(id, _ => true));
        Assert.Equal(PluginChange.Installed, PluginChangedEvent.InstallOrUpdate(id, _ => false));
    }

    /// <summary>
    /// Only a failure is worth a notification, and only from a task Jellyfin itself reports
    /// on. A task that asks not to be logged is one that runs constantly.
    /// </summary>
    [Theory]
    [InlineData(TaskCompletionStatus.Failed, true, true)]
    [InlineData(TaskCompletionStatus.Completed, true, false)]
    [InlineData(TaskCompletionStatus.Cancelled, true, false)]
    [InlineData(TaskCompletionStatus.Aborted, true, false)]
    [InlineData(TaskCompletionStatus.Failed, false, false)]
    public void OnlyAFailureWorthReportingIsToldAbout(TaskCompletionStatus status, bool logged, bool expected)
    {
        Assert.Equal(expected, TaskFailedService.WorthTelling(status, new FakeTask(logged)));
    }

    /// <summary>
    /// A task the server gave no details about is reported all the same.
    /// </summary>
    [Fact]
    public void ATaskWithNothingKnownAboutItIsStillAFailure()
    {
        Assert.True(TaskFailedService.WorthTelling(TaskCompletionStatus.Failed, null));
    }

    private sealed class FakeTask(bool logged) : IScheduledTask, IConfigurableScheduledTask
    {
        public string Name => "Fake";

        public string Key => "Fake";

        public string Description => "Fake";

        public string Category => "Fake";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged { get; } = logged;

        public System.Collections.Generic.IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

        public System.Threading.Tasks.Task ExecuteAsync(System.IProgress<double> progress, System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
