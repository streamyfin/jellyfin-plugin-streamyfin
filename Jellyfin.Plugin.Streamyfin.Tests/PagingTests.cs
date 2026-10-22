using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Streamyfin.Api;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Handing back one page of something the server answers whole.
/// </summary>
/// <remarks>
/// Jellyfin's own <c>/UserViews</c> ignores <c>startIndex</c> and <c>limit</c>: measured
/// on 10.11.11, asking for two rows from the third of three answers all three with
/// <c>StartIndex: 0</c>. The app's home rows scroll, so a row drawn from it repeats its
/// libraries for as long as somebody keeps scrolling.
/// </remarks>
public class PagingTests
{
    private static readonly string[] Libraries = ["Films", "Kids", "Private", "Music"];

    /// <summary>
    /// A page is what was asked for, and the count is of everything.
    /// </summary>
    [Fact]
    public void APageIsWhatWasAskedFor()
    {
        var page = Paging.Of(Libraries, startIndex: 1, limit: 2);

        Assert.Equal(["Kids", "Private"], page.Items);
        Assert.Equal(4, page.Total);
        Assert.Equal(1, page.StartIndex);
    }

    /// <summary>
    /// Past the end is an empty page rather than the first one again, which is what makes
    /// a scrolling row stop.
    /// </summary>
    [Fact]
    public void PastTheEndIsEmpty()
    {
        Assert.Empty(Paging.Of(Libraries, startIndex: 10, limit: 10).Items);
        Assert.Equal(4, Paging.Of(Libraries, startIndex: 10, limit: 10).Total);
    }

    /// <summary>
    /// Nothing asked for is everything, since that is what a client that knows no better
    /// expects from a list this short.
    /// </summary>
    [Fact]
    public void NothingAskedForIsEverything()
    {
        Assert.Equal(4, Paging.Of(Libraries, startIndex: null, limit: null).Items.Count);
        Assert.Equal(0, Paging.Of(Libraries, startIndex: null, limit: null).StartIndex);
    }

    /// <summary>
    /// What cannot be a page is read as none of it rather than refused: a negative start
    /// is the beginning, and a limit of zero or less is nothing at all.
    /// </summary>
    [Fact]
    public void WhatCannotBeAPageIsReadGently()
    {
        Assert.Equal(["Films", "Kids", "Private", "Music"], Paging.Of(Libraries, startIndex: -5, limit: null).Items);
        Assert.Empty(Paging.Of(Libraries, startIndex: 0, limit: 0).Items);
        Assert.Empty(Paging.Of(Libraries, startIndex: 0, limit: -1).Items);
    }

    /// <summary>
    /// Nothing to page is an empty page and no count, not an error.
    /// </summary>
    [Fact]
    public void NothingToPageIsAnEmptyPage()
    {
        var page = Paging.Of(Array.Empty<string>(), startIndex: 3, limit: 3);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }
}
