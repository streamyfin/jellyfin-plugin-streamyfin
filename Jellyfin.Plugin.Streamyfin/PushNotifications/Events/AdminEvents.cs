using System;
using System.Globalization;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// What happened to a plugin.
/// </summary>
public enum PluginChange
{
    /// <summary>It was installed for the first time.</summary>
    Installed,

    /// <summary>A version replaced another.</summary>
    Updated,

    /// <summary>It was removed.</summary>
    Uninstalled
}

/// <summary>
/// The messages an administrator is sent about the server itself rather than about its
/// library: a scheduled task that failed, a plugin that changed, a sign in that was
/// refused.
/// </summary>
/// <remarks>
/// The wording is Jellyfin's own, taken from the strings its activity log uses for these
/// events in each language this plugin carries, so an administrator reads the same
/// sentence about the same event whether it reaches them through the dashboard or through
/// their phone.
///
/// <para>
/// The culture is a parameter rather than the server's, so a message can be built per
/// device once a device says which language it is in.
/// </para>
/// </remarks>
public static class AdminEvents
{
    /// <summary>
    /// How much of the reason a task gave is carried. A push is a sentence, and Expo
    /// refuses a message past four kilobytes whole, so a stack trace cannot go in one.
    /// </summary>
    internal const int ReasonLimit = 120;

    /// <summary>
    /// A scheduled task that failed.
    /// </summary>
    /// <param name="localization">The strings.</param>
    /// <param name="taskName">The task, as the dashboard names it.</param>
    /// <param name="reason">What it said went wrong, which may be nothing.</param>
    /// <param name="culture">The language to write in, or the server's when null.</param>
    /// <returns>The message to send.</returns>
    public static ExpoNotificationRequest TaskFailed(
        LocalizationHelper localization,
        string taskName,
        string? reason,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var said = FirstLine(reason);

        return new ExpoNotificationRequest
        {
            Title = localization.GetString("TaskFailedTitle", culture),
            Body = said is null
                ? localization.GetFormatted("TaskFailed", culture, taskName.Escape())
                : localization.GetFormatted("TaskFailedWithReason", culture, taskName.Escape(), said)
        };
    }

    /// <summary>
    /// A plugin that was installed, updated or uninstalled.
    /// </summary>
    /// <param name="localization">The strings.</param>
    /// <param name="change">What happened to it.</param>
    /// <param name="name">The plugin.</param>
    /// <param name="version">Its version, which may be unknown.</param>
    /// <param name="culture">The language to write in, or the server's when null.</param>
    /// <returns>The message to send.</returns>
    public static ExpoNotificationRequest PluginChanged(
        LocalizationHelper localization,
        PluginChange change,
        string name,
        string? version,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var known = !string.IsNullOrWhiteSpace(version);

        var title = change switch
        {
            PluginChange.Installed => "PluginInstalledTitle",
            PluginChange.Updated => "PluginUpdatedTitle",
            _ => "PluginUninstalledTitle"
        };

        var body = change switch
        {
            PluginChange.Installed => known ? "PluginInstalledVersion" : "PluginInstalled",
            PluginChange.Updated => known ? "PluginUpdatedVersion" : "PluginUpdated",
            _ => known ? "PluginUninstalledVersion" : "PluginUninstalled"
        };

        return new ExpoNotificationRequest
        {
            Title = localization.GetString(title, culture),
            Body = known
                ? localization.GetFormatted(body, culture, name.Escape(), version)
                : localization.GetFormatted(body, culture, name.Escape())
        };
    }

    /// <summary>
    /// A sign in the server refused.
    /// </summary>
    /// <param name="localization">The strings.</param>
    /// <param name="username">The name that was tried, which the server may not have.</param>
    /// <param name="address">Where it came from, which the server may not have either.</param>
    /// <param name="culture">The language to write in, or the server's when null.</param>
    /// <returns>The message to send.</returns>
    public static ExpoNotificationRequest SignInFailed(
        LocalizationHelper localization,
        string? username,
        string? address,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var named = !string.IsNullOrWhiteSpace(username);
        var from = !string.IsNullOrWhiteSpace(address);

        var key = (named, from) switch
        {
            (true, true) => "SignInFailedFrom",
            (true, false) => "SignInFailed",
            (false, true) => "SignInFailedUnnamedFrom",
            _ => "SignInFailedUnnamed"
        };

        var body = (named, from) switch
        {
            (true, true) => localization.GetFormatted(key, culture, username.Escape(), address.Escape()),
            (true, false) => localization.GetFormatted(key, culture, username.Escape()),
            (false, true) => localization.GetFormatted(key, culture, address.Escape()),
            _ => localization.GetString(key, culture)
        };

        return new ExpoNotificationRequest
        {
            Title = localization.GetString("SignInFailedTitle", culture),
            Body = body
        };
    }

    /// <summary>
    /// The first line of what something said, short enough to read on a lock screen.
    /// </summary>
    /// <param name="text">What was said, which may be a stack trace or nothing at all.</param>
    /// <returns>The first line, cut if it is long, or null when there is nothing to say.</returns>
    internal static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        if (line.Length == 0)
        {
            return null;
        }

        return line.Length <= ReasonLimit ? line : string.Concat(line.AsSpan(0, ReasonLimit).TrimEnd(), "…");
    }
}
