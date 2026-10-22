using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Watches a send nobody waits for, so its end is heard of whichever way it ends.
/// </summary>
internal static class Detached
{
    /// <summary>
    /// Hands a send that failed, or ended canceled, to <paramref name="failed"/>.
    /// </summary>
    /// <param name="send">The send.</param>
    /// <param name="failed">Hears of one that did not go.</param>
    /// <returns>A task that ends with the send and never fails.</returns>
    /// <remarks>
    /// A send Expo answered slower than the client's timeout ends canceled rather than failed,
    /// and only a failed one used to be heard of: the message was lost without a line in the log.
    /// </remarks>
    public static Task Observe(Task send, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(failed);

        return send.ContinueWith(
            task =>
            {
                if (task.Exception is { } failure)
                {
                    failed(failure.InnerExceptions.Count == 1 ? failure.InnerExceptions[0] : failure);
                }
                else if (task.IsCanceled)
                {
                    failed(new TaskCanceledException(task));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
