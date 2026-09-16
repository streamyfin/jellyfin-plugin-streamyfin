using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

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

            if (threshold < 0 || double.IsNaN(threshold.Value) || double.IsInfinity(threshold.Value))
            {
                var key = NotificationsForm.KeyOf(eventProperty);
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key}.recentEventThreshold is {threshold.Value}. A wait is a number of seconds, so it starts at 0."));
            }
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }
}
