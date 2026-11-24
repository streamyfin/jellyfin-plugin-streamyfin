using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// What the server refuses to store about an event.
/// </summary>
/// <remarks>
/// The settings have had this since P5.2; the notifications had nothing. The admin page
/// put a floor of zero on the wait between two of the same event, and the page was the
/// only thing that knew: the Yaml tab writes whatever is typed, so a wait of minus one
/// reached the database and then the scheduler.
/// </remarks>
public static class NotificationsValidation
{
    /// <summary>
    /// The longest wait the server can count, in seconds: a <see cref="System.TimeSpan"/> holds
    /// no more.
    /// </summary>
    internal const double LongestWait = 922_337_203_685;

    /// <summary>
    /// The reason these events cannot be stored, if there is one.
    /// </summary>
    /// <param name="notifications">The events, which may be null.</param>
    /// <returns>The message to refuse with, or <c>null</c> when they can be stored.</returns>
    public static string? Check(Notifications? notifications)
    {
        if (notifications is null)
        {
            return null;
        }

        var problems = new List<string>();

        if (CheckWording(notifications.Wording) is { } wording)
        {
            problems.Add(wording);
        }

        problems.AddRange(OutOfBounds(notifications));

        foreach (var eventProperty in typeof(Notifications).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (eventProperty.GetValue(notifications) is not NotificationConfiguration block)
            {
                continue;
            }

            var threshold = block.RecentEventThreshold;
            if (threshold is null)
            {
                continue;
            }

            var key = NotificationsForm.KeyOf(eventProperty);
            var waits = eventProperty.GetCustomAttribute<RecentEventWaitAttribute>() is not null;

            // The configuration is stored as JSON, which cannot hold a wait that is not a
            // number, so that is refused on every event. A negative one is refused only where
            // the event reads it: the others are not drawn, and one restored from a backup
            // would refuse every save with nowhere to fix it.
            if (double.IsNaN(threshold.Value) || double.IsInfinity(threshold.Value) || (waits && threshold < 0))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key}.recentEventThreshold is {threshold.Value}. A wait is a number of seconds, so it starts at 0."));
            }
            else if (waits && threshold > LongestWait)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key}.recentEventThreshold is {threshold.Value}, longer than the server can count. A wait is at most {LongestWait} seconds."));
            }
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    // Every field of an event that declares its bounds, held to them: the page draws the same
    // bounds from the same attribute, so the two cannot disagree (#289).
    private static IEnumerable<string> OutOfBounds(Notifications notifications)
    {
        foreach (var eventProperty in typeof(Notifications).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (eventProperty.GetValue(notifications) is not NotificationConfiguration block)
            {
                continue;
            }

            foreach (var field in block.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<BoundsAttribute>() is not { } bounds || field.GetValue(block) is not double value)
                {
                    continue;
                }

                if (!double.IsFinite(value) || value < bounds.Minimum || value > bounds.Maximum)
                {
                    var name = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? field.Name;
                    yield return string.Create(
                        CultureInfo.InvariantCulture,
                        $"{NotificationsForm.KeyOf(eventProperty)}.{name} is {value}. It takes a number of seconds from {bounds.Minimum} to {bounds.Maximum}.");
                }
            }
        }
    }

    /// <summary>
    /// The reason what a group or a user says about the events cannot be stored, if there
    /// is one.
    /// </summary>
    /// <param name="said">What the level says, which may be nothing.</param>
    /// <returns>The message to refuse with, or <c>null</c> when it can be stored.</returns>
    /// <remarks>
    /// An event this server does not have is refused rather than kept: a level is read on
    /// every send and a key nobody recognises would be silently skipped there, so a typo
    /// would look exactly like a setting that does not work.
    /// </remarks>
    public static string? CheckTargeting(IReadOnlyDictionary<string, NotificationTargeting>? said)
    {
        if (said is null)
        {
            return null;
        }

        var known = NotificationsForm.Events().Select(one => one.Key).ToHashSet(System.StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var (key, targeting) in said)
        {
            if (!known.Contains(key))
            {
                problems.Add($"{key} is not an event this server has.");
                continue;
            }

            // Nothing reads a level's wait: an event waits what the server says. The Targeting
            // tab draws no field for it, so a negative one a level kept is left alone rather
            // than refusing every save of that level. It is still stored as JSON, which cannot
            // hold one that is not a number.
            var wait = targeting?.RecentEventThreshold;

            if (wait is not null && (double.IsNaN(wait.Value) || double.IsInfinity(wait.Value)))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key}.recentEventThreshold is {wait.Value}. A wait is a number of seconds, so it starts at 0."));
            }
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>
    /// The reason a wording cannot be stored, if there is one.
    /// </summary>
    /// <param name="said">What an administrator wants said instead, which may be nothing.</param>
    /// <returns>The message to refuse with, or <c>null</c> when it can be stored.</returns>
    /// <remarks>
    /// Two mistakes are caught here rather than when the event fires: a sentence this
    /// server does not have, which would be read and skipped on every send, and a wording
    /// asking for a placeholder the sentence it replaces does not have, which would throw
    /// inside an event handler the server is waiting on.
    /// </remarks>
    public static string? CheckWording(IEnumerable<WordingOverride>? said)
    {
        if (said is null)
        {
            return null;
        }

        var problems = new List<string>();

        foreach (var one in said)
        {
            if (one is null || string.IsNullOrWhiteSpace(one.Text))
            {
                continue;
            }

            var sentence = Wording.Known(one.Key);

            if (sentence is null)
            {
                problems.Add($"{one.Key} is not a sentence this server writes.");
                continue;
            }

            if (Wording.AsksForWhatCannotBeAPlaceholder(one.Text))
            {
                problems.Add($"The wording for {one.Key} carries a number too big to be a placeholder.");
                continue;
            }

            var asks = Wording.Asks(one.Text);

            if (asks > sentence.Placeholders)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The wording for {one.Key} asks for {{{asks - 1}}}, and that sentence names {sentence.Placeholders} thing(s)."));
            }
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }
}
