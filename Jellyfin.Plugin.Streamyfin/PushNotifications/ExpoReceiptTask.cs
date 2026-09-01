using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Collects the delivery receipts of pushes Expo accepted, and removes the tokens it
/// reports as gone.
/// </summary>
/// <remarks>
/// This is the half of P4.2 that cannot happen during a send. Expo answers a send with a
/// ticket, which only says the message was queued; the receipt saying whether it arrived
/// is produced minutes later. Nothing ever asked for one, so a device that uninstalled
/// the app kept its row forever and every notification aimed at it was thrown away by
/// Expo with nothing to show for it.
///
/// <para>
/// A scheduled task rather than a timer inside the plugin: it survives a restart through
/// the stored rows, an administrator can see it in the dashboard, run it by hand, and
/// read why it did what it did.
/// </para>
///
/// <para>
/// Jellyfin discovers this by the interface, so nothing registers it.
/// </para>
/// </remarks>
public class ExpoReceiptTask : IScheduledTask
{
    /// <summary>
    /// How long a push is left alone before its receipt is asked for. Expo's own guidance
    /// is to wait; asking immediately mostly returns nothing and burns the rate limit.
    /// </summary>
    private static readonly TimeSpan _ripeAfter = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a push is kept at all. Expo keeps a receipt for 24 hours, so past that
    /// the answer is never coming and the row is only another thing accumulating.
    /// </summary>
    private static readonly TimeSpan _expiresAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// Expo takes a thousand ticket ids per request, so that is one run's worth.
    /// </summary>
    private const int BatchSize = 1000;

    private readonly NotificationHelper _notifications;
    private readonly ILogger<ExpoReceiptTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpoReceiptTask"/> class.
    /// </summary>
    /// <param name="notifications">The helper that talks to Expo.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public ExpoReceiptTask(NotificationHelper notifications, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _notifications = notifications;
        _logger = loggerFactory.CreateLogger<ExpoReceiptTask>();
    }

    /// <inheritdoc />
    public string Name => "Streamyfin push receipts";

    /// <inheritdoc />
    public string Key => "Jellyfin.Plugin.Streamyfin.ExpoReceipts";

    /// <inheritdoc />
    public string Description =>
        "Asks Expo what became of the notifications it accepted, and forgets the devices it reports as no longer registered.";

    /// <inheritdoc />
    public string Category => "Streamyfin";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(1).Ticks
        }
    ];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var database = StreamyfinPlugin.Instance?.Database;

        if (database is null)
        {
            _logger.LogDebug("No plugin database yet, nothing to collect");
            return;
        }

        var now = DateTime.UtcNow;

        // Anything Expo would no longer answer for goes first, so an unreachable Expo or a
        // run that keeps failing cannot let the table grow without bound.
        var expired = database.RemoveExpoReceiptsSentBefore(now - _expiresAfter);
        if (expired > 0)
        {
            _logger.LogInformation(
                "Dropped {Count} push receipt(s) Expo no longer keeps an answer for",
                expired);
        }

        progress?.Report(10);

        var pending = database.GetExpoReceiptsSentBefore(now - _ripeAfter, BatchSize);

        if (pending.Count == 0)
        {
            _logger.LogDebug("No push is waiting on a receipt");
            progress?.Report(100);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var ticketToToken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var receipt in pending)
        {
            ticketToToken[receipt.TicketId] = receipt.Token;
        }

        var response = await _notifications
            .FetchReceipts([.. ticketToToken.Keys])
            .ConfigureAwait(false);

        progress?.Report(70);

        // A refused request is not an answer about anybody's token. The rows stay, and the
        // next run asks again, until they expire on their own.
        if (response is null)
        {
            _logger.LogWarning(
                "Expo did not answer for {Count} push receipt(s), which will be asked for again",
                pending.Count);
            return;
        }

        var dead = ExpoTickets.DeadTokensFrom(response, ticketToToken);

        if (dead.Count > 0)
        {
            var removed = database.RemoveDeviceTokensNamed(dead);

            _logger.LogInformation(
                "Expo reported {Devices} device(s) as no longer registered, {Rows} token row(s) removed",
                dead.Count,
                removed);
        }

        // Every ticket that was answered is done with, whatever it said. One that is still
        // pending on Expo's side is absent from the answer and keeps its row for next time.
        var answered = response.Data.Keys.Where(ticketToToken.ContainsKey).ToList();
        database.RemoveExpoReceipts(answered);

        _logger.LogInformation(
            "Collected {Answered} of {Asked} push receipt(s)",
            answered.Count,
            pending.Count);

        progress?.Report(100);
    }
}
