using System;
using System.Collections.Generic;
using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.PushNotifications.Events;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Whether a person follows a show, as Jellyfin knows it (P4.5, E5).
/// </summary>
public class ShowWatchingTests
{
    private static readonly User Alice = new("alice", "provider", "reset");

    /// <summary>
    /// Started means watched or begun: an episode left halfway is already a show they follow.
    /// </summary>
    [Fact]
    public void AnEpisodeInProgressCountsAsStarted()
    {
        var watching = new LibraryShowWatching(Library(inProgress: true, played: false), DispatchProxy.Create<IUserDataManager, Nothing>());

        Assert.True(watching.HasStarted(Alice, Guid.NewGuid()));
    }

    /// <summary>
    /// An episode watched to the end counts as started.
    /// </summary>
    [Fact]
    public void AnEpisodeWatchedToTheEndCountsAsStarted()
    {
        var watching = new LibraryShowWatching(Library(inProgress: false, played: true), DispatchProxy.Create<IUserDataManager, Nothing>());

        Assert.True(watching.HasStarted(Alice, Guid.NewGuid()));
    }

    /// <summary>
    /// A show never opened is not started.
    /// </summary>
    [Fact]
    public void AShowNeverOpenedIsNotStarted()
    {
        var watching = new LibraryShowWatching(Library(inProgress: false, played: false), DispatchProxy.Create<IUserDataManager, Nothing>());

        Assert.False(watching.HasStarted(Alice, Guid.NewGuid()));
    }

    /// <summary>
    /// A library holding one episode, in progress, played, or neither.
    /// </summary>
    private static ILibraryManager Library(bool inProgress, bool played)
    {
        var library = DispatchProxy.Create<ILibraryManager, EpisodesStub>();
        ((EpisodesStub)(object)library).InProgress = inProgress;
        ((EpisodesStub)(object)library).Played = played;
        return library;
    }

    /// <summary>
    /// An <see cref="ILibraryManager"/> holding one episode, which is in progress, played, or
    /// neither, and answering only the episode queries.
    /// </summary>
    public class EpisodesStub : DispatchProxy
    {
        /// <summary>Gets or sets a value indicating whether the episode is in progress.</summary>
        public bool InProgress { get; set; }

        /// <summary>Gets or sets a value indicating whether the episode was played to the end.</summary>
        public bool Played { get; set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemList) && args is [InternalItemsQuery query, ..])
            {
                var matches = (query.IsResumable == true && InProgress) || (query.IsPlayed == true && Played);
                return matches ? new List<BaseItem> { new Episode { Id = Guid.NewGuid() } } : new List<BaseItem>();
            }

            var returns = targetMethod?.ReturnType;
            return returns is { IsValueType: true } && returns != typeof(void) ? Activator.CreateInstance(returns) : null;
        }
    }

    /// <summary>
    /// A stand-in that answers nothing.
    /// </summary>
    public class Nothing : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returns = targetMethod?.ReturnType;
            return returns is { IsValueType: true } && returns != typeof(void) ? Activator.CreateInstance(returns) : null;
        }
    }
}
