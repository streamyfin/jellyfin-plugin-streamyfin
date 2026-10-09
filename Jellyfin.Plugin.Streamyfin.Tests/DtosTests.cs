using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Streamyfin.Compat;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// For You and My Media write their items through <see cref="Dtos.Of"/>, which reaches the
/// server's DTO service on the line the build runs on.
/// </summary>
/// <remarks>
/// What these pin is what each build hands the server. A 10.11 build running on 12, the case
/// <see cref="Dtos"/> exists for, cannot be built here: it needs the 10.11 build beside the 12
/// assemblies, which is what the binary check of the release campaign does.
/// </remarks>
public class DtosTests
{
    /// <summary>
    /// The items, the options and the user reach the server's DTO service as they were given,
    /// and its answer is the one returned.
    /// </summary>
    [Fact]
    public void TheItemsReachTheServerAsTheyWereGiven()
    {
        var service = DispatchProxy.Create<IDtoService, Recording>();
        var recording = (Recording)(object)service;
        var items = new List<BaseItem> { new Movie { Name = "Sintel" } };
        var options = new DtoOptions();
        var user = new User("alex", "Default", "Default");

        var written = Dtos.Of(service, items, options, user);

        var call = Assert.Single(recording.Calls);
        Assert.Equal(nameof(IDtoService.GetBaseItemDtos), call.Method);
        Assert.Same(items, call.Arguments[0]);
        Assert.Same(options, call.Arguments[1]);
        Assert.Same(user, call.Arguments[2]);
        Assert.Same(Recording.Answer, written);
    }

    /// <summary>
    /// On Jellyfin 12, where the method has a fifth parameter, the visibility check is kept: a
    /// user is never written an item from a library they cannot open.
    /// </summary>
    [Fact]
    public void TheVisibilityCheckIsNeverSkipped()
    {
        var service = DispatchProxy.Create<IDtoService, Recording>();

        Dtos.Of(service, [], new DtoOptions(), null);

        var call = Assert.Single(((Recording)(object)service).Calls);
        Assert.DoesNotContain(true, call.Arguments.OfType<bool>());
    }

    /// <summary>
    /// An <see cref="IDtoService"/> that records each call and answers with one fixed list.
    /// </summary>
    public class Recording : DispatchProxy
    {
        /// <summary>
        /// Gets what every call answers.
        /// </summary>
        public static IReadOnlyList<BaseItemDto> Answer { get; } = [new BaseItemDto { Name = "written" }];

        /// <summary>
        /// Gets the calls, by method name, with their arguments.
        /// </summary>
        public List<(string Method, object?[] Arguments)> Calls { get; } = [];

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add((targetMethod?.Name ?? string.Empty, args ?? []));
            return targetMethod?.ReturnType == typeof(IReadOnlyList<BaseItemDto>) ? Answer : null;
        }
    }
}
