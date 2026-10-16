using System;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What the app sends to wait for a title, and what it reads back (#225).
/// </summary>
public class AwaitedTitleModelsTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private readonly SerializationHelper _serialization = new();

    /// <summary>
    /// A request becomes a row for the caller, its name trimmed.
    /// </summary>
    [Fact]
    public void ARequestBecomesARowForTheCaller()
    {
        var alice = Guid.NewGuid();
        var request = _serialization.DeserializeJson<AwaitTitleRequest>(
            """{"mediaType":"tv","tmdbId":1399,"tvdbId":121361,"title":"  Game of Thrones ","year":2011}""");

        var row = request!.ToRow(alice, Now);

        Assert.Equal(alice, row.UserId);
        Assert.Equal("tv", row.MediaType);
        Assert.Equal(1399, row.TmdbId);
        Assert.Equal(121361, row.TvdbId);
        Assert.Equal("Game of Thrones", row.Title);
        Assert.Equal(2011, row.Year);
        Assert.Equal(Now, row.AddedAt);
        Assert.Null(row.ArrivedItemId);
    }

    /// <summary>
    /// The app reads whether a title already arrived and waits for a pause to end.
    /// </summary>
    [Fact]
    public void TheAppReadsWhetherATitleAlreadyArrived()
    {
        var dto = AwaitedTitleDto.From(new AwaitedTitle
        {
            UserId = Guid.NewGuid(),
            MediaType = "movie",
            TmdbId = 603,
            Title = "The Matrix",
            AddedAt = Now,
            ArrivedItemId = Guid.NewGuid()
        });

        var json = _serialization.SerializeToJson(dto);
        // Read rather than matched as text: a Debug build writes the same JSON indented.
        using var read = JsonDocument.Parse(json);

        Assert.True(dto.Arrived);
        Assert.True(read.RootElement.GetProperty("arrived").GetBoolean());
        Assert.Equal(603, read.RootElement.GetProperty("tmdbId").GetInt32());
        Assert.DoesNotContain("userId", json, StringComparison.Ordinal);
    }
}
