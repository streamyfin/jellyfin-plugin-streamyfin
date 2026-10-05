using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The line a media notification ends with, naming the item.
/// </summary>
public class MediaNotificationTests
{
    /// <summary>
    /// An episode with a number but no season is named by its series and number.
    /// </summary>
    /// <remarks>
    /// The helper asked for "SeriesEpisode" while the resource is "Series Episode", and a
    /// missing resource comes back as its key, so this notification read "SeriesEpisode".
    /// </remarks>
    [Fact]
    public void AnEpisodeWithoutASeasonIsNamedBySeriesAndNumber()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "The Office" };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = series.Id, IndexNumber = 5 };

        var notification = WithLibrary(
            [series],
            () => MediaNotificationHelper.CreateMediaNotification(
                new LocalizationHelper(null, null),
                "Episode added",
                [],
                episode,
                CultureInfo.InvariantCulture));

        Assert.NotNull(notification);
        Assert.Equal("The Office, Episode 05", notification.Body);
    }

    // Episode.Series and Episode.Season go through the static BaseItem.LibraryManager,
    // which the server sets at startup. Only lookups by id are answered here.
    private static T WithLibrary<T>(IEnumerable<BaseItem> items, Func<T> act)
    {
        var library = DispatchProxy.Create<ILibraryManager, LibraryStub>();
        foreach (var item in items)
        {
            ((LibraryStub)(object)library).Items[item.Id] = item;
        }

        var previous = BaseItem.LibraryManager;
        BaseItem.LibraryManager = library;
        try
        {
            return act();
        }
        finally
        {
            BaseItem.LibraryManager = previous;
        }
    }

    /// <summary>
    /// An <see cref="ILibraryManager"/> that knows a few items by id and nothing else.
    /// </summary>
    public class LibraryStub : DispatchProxy
    {
        /// <summary>
        /// The items it answers for.
        /// </summary>
        public Dictionary<Guid, BaseItem> Items { get; } = [];

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemById) && args is [Guid id, ..])
            {
                return Items.GetValueOrDefault(id);
            }

            var returns = targetMethod?.ReturnType;
            return returns is { IsValueType: true } && returns != typeof(void) ? Activator.CreateInstance(returns) : null;
        }
    }
}
