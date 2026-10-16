using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Tells the people who paused their notifications about the titles they waited for that
/// arrived meanwhile, once the pause is over (#225).
/// </summary>
public class AwaitedTitlesTask : IScheduledTask
{
    private readonly AwaitedTitleAnnouncer _announcer;
    private readonly ILogger<AwaitedTitlesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwaitedTitlesTask"/> class.
    /// </summary>
    /// <param name="announcer">Settles what a pause held.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public AwaitedTitlesTask(AwaitedTitleAnnouncer announcer, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _announcer = announcer;
        _logger = loggerFactory.CreateLogger<AwaitedTitlesTask>();
    }

    /// <inheritdoc />
    public string Name => "Streamyfin awaited titles";

    /// <inheritdoc />
    public string Key => "Jellyfin.Plugin.Streamyfin.AwaitedTitles";

    /// <inheritdoc />
    public string Description =>
        "Tells the people who paused their notifications about the titles they waited for that arrived meanwhile.";

    /// <inheritdoc />
    public string Category => "Streamyfin";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromMinutes(15).Ticks
        }
    ];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var told = await _announcer.SettleHeld().ConfigureAwait(false);

        if (told > 0)
        {
            _logger.LogInformation("Told {Count} person(s) about a title that arrived during their pause", told);
        }

        progress.Report(100);
    }
}
