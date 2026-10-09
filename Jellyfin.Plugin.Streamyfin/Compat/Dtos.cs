#if JF11
using System;
using System.Runtime.CompilerServices;
#endif
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Streamyfin.Compat;

/// <summary>
/// Items written the way the app reads them, by the server's own DTO service.
/// </summary>
internal static class Dtos
{
#if JF11
    // The 10.11 build runs on 12 as well: an administrator who moves Jellyfin to 12 after
    // installing it keeps it, since the catalogue offers no other build of the same version.
    // 12 gave GetBaseItemDtos a fifth parameter, skipVisibilityCheck, with a default, and a
    // default only exists when compiling: the four parameter method this build is compiled
    // against is not there, and For You and My Media answered 500. On 12 the method it has
    // instead is reached through this delegate, found once. On 10.11 there is none.
    private static readonly Func<IDtoService, IReadOnlyList<BaseItem>, DtoOptions, User?, BaseItem?, bool, IReadOnlyList<BaseItemDto>>? Twelve =
        typeof(IDtoService)
            .GetMethod(
                nameof(IDtoService.GetBaseItemDtos),
                [typeof(IReadOnlyList<BaseItem>), typeof(DtoOptions), typeof(User), typeof(BaseItem), typeof(bool)])
            ?.CreateDelegate<Func<IDtoService, IReadOnlyList<BaseItem>, DtoOptions, User?, BaseItem?, bool, IReadOnlyList<BaseItemDto>>>();
#endif

    /// <summary>
    /// The DTOs of the items among <paramref name="items"/> that <paramref name="user"/> may see.
    /// </summary>
    /// <param name="dtoService">The server's DTO service.</param>
    /// <param name="items">The items.</param>
    /// <param name="options">What to write about them.</param>
    /// <param name="user">The user they are written for.</param>
    /// <returns>A DTO for each item the user may see, in order. The server leaves out the others.</returns>
    public static IReadOnlyList<BaseItemDto> Of(IDtoService dtoService, IReadOnlyList<BaseItem> items, DtoOptions options, User? user)
    {
#if JF11
        // skipVisibilityCheck stays false, so the libraries a user cannot open stay out.
        return Twelve is { } twelve
            ? twelve(dtoService, items, options, user, null, false)
            : TenEleven(dtoService, items, options, user);
#else
        return dtoService.GetBaseItemDtos(items, options, user);
#endif
    }

#if JF11
    // A method of its own, so that on 12, where the method it calls does not exist, it is never
    // compiled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<BaseItemDto> TenEleven(IDtoService dtoService, IReadOnlyList<BaseItem> items, DtoOptions options, User? user) =>
        dtoService.GetBaseItemDtos(items, options, user);
#endif
}
