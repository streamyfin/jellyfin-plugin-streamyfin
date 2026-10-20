using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// Marks an event that keeps two of the same apart, with how long it waits when nothing
/// sets a wait.
/// </summary>
/// <remarks>
/// Only these events read <see cref="NotificationConfiguration.RecentEventThreshold"/>, and
/// only the server's own. The Notifications tab offered a wait on every event, so one set on
/// a new item, an awaited title or a locked account changed nothing, and an empty one did not
/// say what the event would wait. It now offers it where this attribute is, with its length.
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

    // Read once: the declarations do not change while the server runs.
    private static readonly Lazy<IReadOnlyDictionary<string, double>> _byEvent = new(() =>
        typeof(Notifications).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (
                // Keyed the way the form keys the event, so the two cannot disagree.
                Key: NotificationsForm.KeyOf(property),
                Wait: property.GetCustomAttribute<RecentEventWaitAttribute>()))
            .Where(one => one.Wait is not null)
            .ToDictionary(one => one.Key, one => one.Wait!.Seconds));

    /// <summary>
    /// What an event waits when nothing sets a wait, read from its declaration, so the events
    /// wait what the Notifications tab says.
    /// </summary>
    /// <param name="eventKey">The event, by the key the configuration uses.</param>
    /// <returns>The wait in seconds.</returns>
    /// <exception cref="ArgumentException">The event declares no wait.</exception>
    public static double SecondsFor(string eventKey) =>
        _byEvent.Value.TryGetValue(eventKey, out var seconds)
            ? seconds
            : throw new ArgumentException($"{eventKey} declares no wait.", nameof(eventKey));

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
