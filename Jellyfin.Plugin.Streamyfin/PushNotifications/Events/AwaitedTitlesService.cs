using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Watches the library for the titles people wait for (#225).
/// </summary>
public class AwaitedTitlesService : BaseEvent, IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly AwaitedTitleAnnouncer _announcer;
    private readonly PendingTitles _pending = new(TimeSpan.FromHours(24));

    /// <summary>
    /// Initializes a new instance of the <see cref="AwaitedTitlesService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library it watches.</param>
    /// <param name="announcer">Settles and announces an arrival.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="localization">The localization helper.</param>
    /// <param name="applicationHost">The server.</param>
    /// <param name="notificationHelper">The notification helper.</param>
    public AwaitedTitlesService(
        ILibraryManager libraryManager,
        AwaitedTitleAnnouncer announcer,
        ILoggerFactory loggerFactory,
        LocalizationHelper localization,
        IServerApplicationHost applicationHost,
        NotificationHelper notificationHelper)
        : base(loggerFactory, localization, applicationHost, notificationHelper)
    {
        _libraryManager = libraryManager;
        _announcer = announcer;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _pending.Clear();
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs args)
    {
        if (AwaitedTitleAnnouncer.TitleOf(args.Item) is { } title)
        {
            Consider(title);
        }
    }

    // A title whose ids came with its metadata, after it was added.
    private void OnItemUpdated(object? sender, ItemChangeEventArgs args)
    {
        if (_pending.Has(args.Item.Id, DateTime.UtcNow))
        {
            Consider(args.Item);
        }
    }

    private void Consider(BaseItem title)
    {
        try
        {
            if (_announcer.Arrive(title) is { } sending)
            {
                _pending.Take(title.Id, DateTime.UtcNow);
                SendDetached(sending, "awaited title");
                return;
            }

            // No id yet: the metadata brings it.
            _pending.Add(title.Id, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            // A library event must not fail the scan that raised it.
            _logger.LogError(e, "Could not settle the arrival of {Title}", title.Name.Escape());
        }
    }
}
