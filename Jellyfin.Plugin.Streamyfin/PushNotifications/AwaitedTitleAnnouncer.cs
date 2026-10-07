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

        return told.Count == 0 ? Task.CompletedTask : Announce(database, title, told);
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

        await AnnounceEach(
            told.GroupBy(row => row.ArrivedItemId!.Value),
            arrival => Announce(database, titles[arrival.Key], [.. arrival]),
            (arrival, e) => _logger.LogError(e, "Could not announce {Title} after a pause", titles[arrival.Key].Name.Escape()))
            .ConfigureAwait(false);

        return told.Count;
    }

    /// <summary>
    /// Announces each arrival in turn, the next one even when one fails.
    /// </summary>
    /// <typeparam name="T">An arrival.</typeparam>
    /// <param name="arrivals">The arrivals, settled already.</param>
    /// <param name="announce">Sends one.</param>
    /// <param name="failed">Hears of one that could not be sent.</param>
    /// <returns>A task.</returns>
    /// <remarks>
    /// The rows are settled before anything is sent, so a failure that stopped the loop lost
    /// every arrival after it for good.
    /// </remarks>
    internal static async Task AnnounceEach<T>(IEnumerable<T> arrivals, Func<T, Task> announce, Action<T, Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(arrivals);
        ArgumentNullException.ThrowIfNull(announce);
        ArgumentNullException.ThrowIfNull(failed);

        foreach (var arrival in arrivals)
        {
            try
            {
                await announce(arrival).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                failed(arrival, e);
            }
        }
    }

    /// <summary>
    /// The message for one audience: the title with its year, opening its page.
    /// </summary>
    /// <param name="title">The movie or the show.</param>
    /// <param name="awaited">What the person asked for.</param>
    /// <param name="audience">The language and the address of the devices.</param>
    /// <returns>The message.</returns>
    internal ExpoNotificationRequest Message(BaseItem title, AwaitedTitle awaited, Audience audience)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(awaited);
        ArgumentNullException.ThrowIfNull(audience);

        // The title the person asked for, as Seerr named it. Until its metadata comes, Jellyfin
        // names an item by its folder, year and ids included.
        var name = awaited.Year is { } year
            ? _localization.GetFormatted(key: "NameAndYear", cultureInfo: audience.Culture, args: [awaited.Title.Escape(), year])
            : awaited.Title.Escape();

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

    /// <summary>
    /// The messages an arrival sends: one per title as the people asked for it, each to those
    /// people only.
    /// </summary>
    /// <param name="told">The rows to announce.</param>
    /// <returns>Each title as it was asked for, with who asked for it that way.</returns>
    /// <remarks>
    /// A row's title is text the person's own app sent. Named by one row and sent to everyone
    /// who waited, it let one person choose the words another received.
    /// </remarks>
    internal static IEnumerable<(AwaitedTitle Named, HashSet<Guid> People)> MessagesFor(IEnumerable<AwaitedTitle> told) =>
        told
            .GroupBy(row => (row.Title, row.Year))
            .Select(same => (same.First(), same.Select(row => row.UserId).ToHashSet()));

    private async Task Announce(PluginDatabase database, BaseItem title, List<AwaitedTitle> told)
    {
        var devices = database.GetAllDeviceTokens();

        foreach (var (named, people) in MessagesFor(told))
        {
            _logger.LogInformation("{Title} arrived for {People} person(s) waiting for it", title.Name.Escape(), people.Count);

            await _notificationHelper.SendToDevices(
                devices.Where(device => people.Contains(device.UserId)).ToList(),
                audience => [Message(title, named, audience)],
                new NotificationSubject(NotificationEvents.AwaitedTitle)).ConfigureAwait(false);
        }
    }
}
