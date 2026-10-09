using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// The four families notifications come in. Each is an Android channel of its own, which the
/// person can also tune in the system settings (E1).
/// </summary>
public static class NotificationFamilies
{
    /// <summary>New movies and episodes.</summary>
    public const string NewContent = "new-content";

    /// <summary>What happens to the person's own Seerr requests.</summary>
    public const string Requests = "requests";

    /// <summary>The person's own account.</summary>
    public const string Account = "account";

    /// <summary>What administrators are told about the server.</summary>
    public const string ServerAlerts = "server-alerts";

    /// <summary>
    /// The family an event belongs to.
    /// </summary>
    /// <param name="eventKey">The event, by the key the levels use.</param>
    /// <returns>Its family; an event about the server is a server alert.</returns>
    public static string Of(string eventKey) => eventKey switch
    {
        NotificationEvents.ItemAdded => NewContent,
        NotificationEvents.SeerrRequests or NotificationEvents.AwaitedTitle => Requests,
        NotificationEvents.UserLockedOut => Account,
        _ => ServerAlerts
    };
}

/// <summary>
/// The events a person can keep or turn off.
/// </summary>
public static class NotificationEvents
{
    /// <summary>A new movie or new episodes.</summary>
    public const string ItemAdded = "itemAdded";

    /// <summary>The person's account was locked.</summary>
    public const string UserLockedOut = "userLockedOut";

    /// <summary>
    /// The person's Seerr request was approved, declined or became available. Known to the
    /// person only: the levels never mention it, so it is not switched off on a server whose
    /// stored configuration predates it.
    /// </summary>
    public const string SeerrRequests = "seerrRequests";

    /// <summary>
    /// A Seerr request waits, was approved by itself, or failed. For administrators, and known
    /// to the person only for the same reason.
    /// </summary>
    public const string SeerrPending = "seerrPending";

    /// <summary>
    /// A title the person asked to be told about arrived (#225). On unless an administrator
    /// turns it off: the person asks for each title themselves.
    /// </summary>
    public const string AwaitedTitle = "awaitedTitle";

    /// <summary>
    /// The events about the server, which only administrators get.
    /// </summary>
    public static readonly IReadOnlyList<string> ForAdministrators =
        [SeerrPending, "sessionStarted", "playbackStarted", "signInFailed", "taskFailed", "pluginChanged"];

    /// <summary>
    /// Every event, in the order the app shows them.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
        [ItemAdded, SeerrRequests, AwaitedTitle, UserLockedOut, .. ForAdministrators];
}

/// <summary>
/// What a message is about, which decides who keeps it and how it stacks.
/// </summary>
/// <param name="EventKey">The event, by the key the levels use.</param>
/// <param name="LibraryId">The library a new item went into, when there is one.</param>
/// <param name="SeriesId">The show new episodes belong to, when there is one.</param>
public sealed record NotificationSubject(string EventKey, Guid? LibraryId = null, Guid? SeriesId = null)
{
    /// <summary>Gets the family, which is the Android channel.</summary>
    public string Family => NotificationFamilies.Of(EventKey);

    /// <summary>
    /// Gets what the message stacks under on iOS: one show's episodes together, the rest by
    /// family (E2).
    /// </summary>
    public string ThreadId => SeriesId is { } series ? $"series-{series:N}" : Family;

    /// <summary>
    /// Gets the buttons the message offers: an episode can also turn its show off (E8).
    /// </summary>
    public string CategoryId => SeriesId is null ? "general" : "episode";
}
