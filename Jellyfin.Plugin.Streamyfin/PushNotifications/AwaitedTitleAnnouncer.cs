using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// Tells the people who waited for a title that it arrived (#225).
/// </summary>
/// <remarks>
/// Apart from the new items: those stop when an administrator turns new items off, and these
/// do not, since each one is a title somebody asked for by name. Every row is settled in the
/// database's write lock before anything is sent, so a title is announced once to each person.
/// </remarks>
public class AwaitedTitleAnnouncer
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly NotificationHelper _notificationHelper;
    private readonly LocalizationHelper _localization;
    private readonly ILogger<AwaitedTitleAnnouncer> _logger;

    /// <summary>
    /// Initializes a new instance of the announcer.
    /// </summary>
    /// <param name="libraryManager">Reads back what a pause held.</param>
    /// <param name="userManager">Finds the people who waited.</param>
    /// <param name="notificationHelper">Sends to their devices.</param>
    /// <param name="localization">Writes the message in each device's language.</param>
    /// <param name="logger">The logger.</param>
    public AwaitedTitleAnnouncer(
        ILibraryManager libraryManager,
        IUserManager userManager,
        NotificationHelper notificationHelper,
        LocalizationHelper localization,
        ILogger<AwaitedTitleAnnouncer> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _notificationHelper = notificationHelper;
        _localization = localization;
        _logger = logger;
    }

    /// <summary>
    /// The title an added item makes available: a movie, or the show of an episode, since a
    /// show counts as arrived with its first episode.
    /// </summary>
    /// <param name="item">What was added.</param>
    /// <returns>The title, or <c>null</c> for anything else.</returns>
    internal static BaseItem? TitleOf(BaseItem item) => item switch
    {
        Movie { IsVirtualItem: false } movie => movie,
        Episode { IsVirtualItem: false } episode => episode.Series,
        _ => null
    };

    /// <summary>
    /// Settles a title's arrival for everyone who waited for it, and tells those it is for now.
    /// </summary>
    /// <param name="title">The movie or the show that arrived.</param>
    /// <returns>
    /// The send, or <c>null</c> when the title has no id yet and its metadata has to bring one.
    /// </returns>
    public Task? Arrive(BaseItem title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var database = StreamyfinPlugin.Instance?.Database;
        if (database is null)
        {
            return Task.CompletedTask;
        }

        var (tmdb, tvdb) = AwaitedTitles.IdsOf(title.ProviderIds);
        if (tmdb is null && tvdb is null)
        {
            return null;
        }

        // Built on the first waiting row only: most arrivals find nobody waiting.
        var decide = new Lazy<Func<AwaitedTitle, BaseItem, AwaitedOutcome>>(() => Deciding(database));
        var mediaType = title is Movie ? AwaitedTitles.Movie : AwaitedTitles.Tv;
        var told = database.SettleArrival(mediaType, tmdb, tvdb, title.Id, row => decide.Value(row, title));

        return told.Count == 0 ? Task.CompletedTask : Announce(database, title, told.Select(row => row.UserId));
    }

    /// <summary>
    /// Tells the people whose pause held an arrival back, once the pause is over.
    /// </summary>
    /// <returns>How many were told.</returns>
    public async Task<int> SettleHeld()
    {
        var database = StreamyfinPlugin.Instance?.Database;
        if (database is null)
        {
            return 0;
        }

        var decide = Deciding(database);
        var titles = new Dictionary<Guid, BaseItem>();
        var told = database.SettleHeld(row =>
        {
            // Gone since it arrived: wait for it again.
            if (row.ArrivedItemId is not { } id || _libraryManager.GetItemById(id) is not { } title)
            {
                return AwaitedOutcome.KeepWaiting;
            }

            titles[id] = title;
            return decide(row, title);
        });

        foreach (var arrival in told.GroupBy(row => row.ArrivedItemId!.Value))
        {
            await Announce(database, titles[arrival.Key], arrival.Select(row => row.UserId)).ConfigureAwait(false);
        }

        return told.Count;
    }

    /// <summary>
    /// The message for one audience: the title with its year, opening its page.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <param name="audience">The language and the address of the devices.</param>
    /// <returns>The message.</returns>
    internal ExpoNotificationRequest Message(BaseItem title, Audience audience)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(audience);

        var name = title.ProductionYear is { } year
            ? _localization.GetFormatted(key: "NameAndYear", cultureInfo: audience.Culture, args: [title.Name.Escape(), year])
            : title.Name.Escape();

        return new ExpoNotificationRequest
        {
            Title = _localization.GetString("AwaitedTitleArrivedTitle", audience.Culture),
            Body = _localization.GetFormatted(key: "AwaitedTitleArrivedBody", cultureInfo: audience.Culture, args: [name]),
            // What the app opens: a movie's page, or a show's the way a batch of episodes
            // names it, which every app that opens notifications already knows.
            Data = title is Movie
                ? new Dictionary<string, object?> { ["id"] = title.Id.ToString("N"), ["type"] = "Movie" }
                : new Dictionary<string, object?> { ["seriesId"] = title.Id.ToString("N"), ["type"] = "Episode" },
            RichContent = DeviceServer.PosterOf(audience.ServerUrl, title.Id) is { } image
                ? new ExpoRichContent { Image = image }
                : null
        };
    }

    // One question per waiting row, with what every row shares read once.
    private Func<AwaitedTitle, BaseItem, AwaitedOutcome> Deciding(PluginDatabase database)
    {
        var targets = NotificationTargets.From(database);
        var preferences = database.AllNotificationPreferences();
        var serverOn = StreamyfinPlugin.Instance?.Settings.Current.notifications?.AwaitedTitle is { Enabled: true };
        var now = DateTime.UtcNow;

        return (row, title) =>
        {
            var user = _userManager.GetUserById(row.UserId);
            var exists = user is not null && !user.IsDisabled();
            var mine = preferences.GetValueOrDefault(row.UserId);

            return AwaitedTitles.Decide(
                personExists: exists,
                reaches: exists
                    && targets.Reaches(NotificationEvents.AwaitedTitle, row.UserId, serverOn, inDefaultAudience: true)
                    && (mine?.Keeps(NotificationEvents.AwaitedTitle) ?? true),
                canOpen: exists && title.IsVisibleStandalone(user!),
                paused: mine?.IsPaused(now) ?? false);
        };
    }

    private Task<ExpoNotificationResponse?> Announce(PluginDatabase database, BaseItem title, IEnumerable<Guid> people)
    {
        var whom = people.ToHashSet();
        var devices = database.GetAllDeviceTokens().Where(device => whom.Contains(device.UserId)).ToList();

        _logger.LogInformation("{Title} arrived for {People} person(s) waiting for it", title.Name.Escape(), whom.Count);

        return _notificationHelper.SendToDevices(
            devices,
            audience => [Message(title, audience)],
            new NotificationSubject(NotificationEvents.AwaitedTitle));
    }
}
