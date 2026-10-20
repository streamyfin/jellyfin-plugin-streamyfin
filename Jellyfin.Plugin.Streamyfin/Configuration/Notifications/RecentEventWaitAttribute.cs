using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// Marks an event that keeps two of the same apart, with how long it waits when nothing
/// sets a wait.
/// </summary>
/// <remarks>
/// Only these events read <see cref="NotificationConfiguration.RecentEventThreshold"/>. The
/// Notifications and Targeting tabs offered a wait on every event, so one set on a new
/// item, an awaited title or a locked account changed nothing, and an empty one did not say
/// what the event would wait. The tabs now offer it where this attribute is, with its length.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RecentEventWaitAttribute : Attribute
{
    /// <summary>
    /// What most events wait, in seconds.
    /// </summary>
    public const double Usual = 5;

    /// <summary>
    /// What two refusals from the same place wait, in seconds. A server anyone can reach is
    /// tried by machines that never stop, and a notification for each of those is a reason
    /// to turn the whole thing off.
    /// </summary>
    public const double SignInFailed = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecentEventWaitAttribute"/> class.
    /// </summary>
    /// <param name="seconds">How long the event waits when nothing sets a wait, in seconds.</param>
    public RecentEventWaitAttribute(double seconds) => Seconds = seconds;

    /// <summary>
    /// Gets how long the event waits when nothing sets a wait, in seconds.
    /// </summary>
    public double Seconds { get; }
}
