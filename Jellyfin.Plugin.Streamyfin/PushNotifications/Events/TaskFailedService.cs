using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Tells the administrators when one of the server's scheduled tasks fails.
/// </summary>
/// <remarks>
/// This listens to <see cref="ITaskManager.TaskCompleted"/> rather than implementing
/// <c>IEventConsumer&lt;TaskCompletionEventArgs&gt;</c>, which is what an event of
/// Jellyfin's normally needs. Nothing publishes that type through the event manager:
/// <c>TaskManager</c> raises the plain event and no one forwards it, on 10.11 and on 12
/// alike, so Jellyfin's own <c>TaskCompletedLogger</c> and the webhook plugin's notifier
/// never run either. The event is the only way to hear about this.
/// </remarks>
public class TaskFailedService : BaseEvent, IHostedService
{
    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskFailedService"/> class.
    /// </summary>
    /// <param name="taskManager">The scheduled tasks, whose completions this listens to.</param>
    /// <param name="loggerFactory">Where the sends are reported.</param>
    /// <param name="localization">The strings.</param>
    /// <param name="applicationHost">The server, for what the base class needs.</param>
    /// <param name="notificationHelper">What sends to the administrators.</param>
    public TaskFailedService(
        ITaskManager taskManager,
        ILoggerFactory loggerFactory,
        LocalizationHelper localization,
        IServerApplicationHost applicationHost,
        NotificationHelper notificationHelper
    ) : base(loggerFactory, localization, applicationHost, notificationHelper)
    {
        _taskManager = taskManager;
    }

    /// <summary>
    /// Whether a completion is one an administrator should hear about.
    /// </summary>
    /// <param name="status">How the task ended.</param>
    /// <param name="task">The task itself, which the server may not name.</param>
    /// <returns><c>true</c> when it failed and is a task Jellyfin itself reports on.</returns>
    /// <remarks>
    /// A task that asks not to be logged is one that runs constantly, and Jellyfin leaves
    /// those out of its activity log for the same reason.
    /// </remarks>
    public static bool WorthTelling(TaskCompletionStatus status, IScheduledTask? task) =>
        status == TaskCompletionStatus.Failed && task is not IConfigurableScheduledTask { IsLogged: false };

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskCompleted += OnTaskCompleted;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskCompleted -= OnTaskCompleted;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override TimeSpan GetRecentEventThreshold() =>
        WaitFrom(Config?.notifications?.TaskFailed, base.GetRecentEventThreshold());

    // Jellyfin raises this event straight from the worker that ran the task, so anything
    // thrown here would land in the server's own task pipeline rather than in a consumer it
    // wraps. A notification that cannot be built is not worth that.
    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs eventArgs)
    {
        try
        {
            Tell(eventArgs);
        }
        catch (Exception thrown)
        {
            _logger.LogError(thrown, "Could not tell the administrators about a scheduled task");
        }
    }

    private void Tell(TaskCompletionEventArgs? eventArgs)
    {
        var result = eventArgs?.Result;

        if (result is null || !WorthTelling(result.Status, eventArgs?.Task?.ScheduledTask))
        {
            return;
        }

        if (Config?.notifications?.TaskFailed is not { Enabled: true })
        {
            _logger.LogInformation("{Task} failed, and notifications about that are off", result.Name);
            return;
        }

        CleanupOldEntries();

        // Per task, so a task that fails every time it runs does not notify on every run.
        if (HasRecentlyProcessed($"task-failed:{result.Key ?? result.Name}"))
        {
            return;
        }

        _logger.LogInformation("{Task} failed, telling the administrators", result.Name);

        SendDetached(
            _notificationHelper.SendToAdmins(
                excludedUserIds: null,
<<<<<<< HEAD
                write: culture => [AdminEvents.TaskFailed(_localization, result.Name, ReasonOf(result), culture)]),
=======
                notifications: AdminEvents.TaskFailed(_localization, result.Name, ReasonOf(result))),
>>>>>>> origin/develop
            "scheduled task failed");
    }

    // The message first, since it is the one written for a person. The stack trace is what
    // is left when a task threw something without one.
    private static string? ReasonOf(TaskResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.LongErrorMessage : result.ErrorMessage;
}
