using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications.Events;

/// <summary>
/// Watches the library for the titles people wait for (#225).
/// </summary>
/// <remarks>
/// What an arrival settles, and the titles watched for their ids, belong to the announcer,
/// which the new item messages settle through too (#289).
/// </remarks>
public class AwaitedTitlesService : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly AwaitedTitleAnnouncer _announcer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwaitedTitlesService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library it watches.</param>
    /// <param name="announcer">Settles and announces an arrival.</param>
    public AwaitedTitlesService(ILibraryManager libraryManager, AwaitedTitleAnnouncer announcer)
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
        _announcer.ForgetPending();
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs args)
    {
        if (AwaitedTitleAnnouncer.TitleOf(args.Item) is { } title)
        {
            _announcer.Consider(title);
        }
    }

    // A title whose ids came with its metadata, after it was added.
    private void OnItemUpdated(object? sender, ItemChangeEventArgs args)
    {
        if (_announcer.SettledBy(args.Item, DateTime.UtcNow) is { } title)
        {
            _announcer.Consider(title);
        }
    }
}
