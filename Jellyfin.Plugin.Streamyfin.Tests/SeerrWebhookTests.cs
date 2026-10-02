using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What a Seerr webhook turns into, and who receives it.
/// </summary>
/// <remarks>
/// The routing is the reason this endpoint exists rather than the JSON template in
/// NOTIFICATIONS.md, so it is what these are about. The case worth naming is a
/// requester the payload does not name: a notification with no target and
/// <c>IsAdmin</c> false goes to every registered device, so "your request was approved"
/// would reach the whole server.
/// </remarks>
public class SeerrWebhookTests
{
    private readonly SeerrNotificationMapper _mapper =
        new(new LocalizationHelper(null, null), NullLoggerFactory.Instance);

    private static SeerrWebhookPayload Payload(string type, string? subject = "Dune", string? requester = "alice") =>
        new()
        {
            NotificationType = type,
            Subject = subject,
            Request = requester is null ? null : new SeerrRequest { RequestedByUsername = requester }
        };

    [Theory]
    [InlineData("MEDIA_PENDING")]
    [InlineData("MEDIA_AUTO_APPROVED")]
    [InlineData("MEDIA_FAILED")]
    [InlineData("TEST_NOTIFICATION")]
    public void WhatTheOperatorActsOnGoesToAdministrators(string type)
    {
        var notification = _mapper.Map(Payload(type));

        Assert.NotNull(notification);
        Assert.True(notification.IsAdmin);
        Assert.Null(notification.Username);
    }

    [Theory]
    [InlineData("MEDIA_APPROVED")]
    [InlineData("MEDIA_DECLINED")]
    [InlineData("MEDIA_AVAILABLE")]
    public void WhatAUserAskedForGoesBackToThatUser(string type)
    {
        var notification = _mapper.Map(Payload(type));

        Assert.NotNull(notification);
        Assert.False(notification.IsAdmin);
        Assert.Equal("alice", notification.Username);
    }

    /// <summary>
    /// The one that matters. Username null and IsAdmin false is how the endpoint is told
    /// to send to every device on the server, so an event naming nobody must not produce
    /// that combination.
    /// </summary>
    [Theory]
    [InlineData("MEDIA_APPROVED")]
    [InlineData("MEDIA_DECLINED")]
    [InlineData("MEDIA_AVAILABLE")]
    [InlineData("SOMETHING_SEERR_ADDED_LATER")]
    public void AnEventNamingNobodyIsNeverSentToEveryone(string type)
    {
        var notification = _mapper.Map(Payload(type, requester: null));

        Assert.NotNull(notification);
        Assert.False(notification.IsAdmin && notification.Username is not null);
        Assert.True(notification.IsAdmin, "with no user to target, this has to go to administrators");
    }

    [Theory]
    [InlineData("ISSUE_CREATED")]
    [InlineData("ISSUE_COMMENT")]
    [InlineData("ISSUE_RESOLVED")]
    [InlineData("ISSUE_REOPENED")]
    public void IssueEventsAreNotHandledHere(string type) =>
        Assert.Null(_mapper.Map(Payload(type)));

    [Fact]
    public void APayloadWithNoEventIsIgnoredRatherThanGuessedAt()
    {
        Assert.Null(_mapper.Map(null));
        Assert.Null(_mapper.Map(new SeerrWebhookPayload()));
        Assert.Null(_mapper.Map(Payload("   ")));
    }

    /// <summary>
    /// Seerr sends these uppercase. Matching case sensitively would turn a lowercase one
    /// into the unknown branch, which routes differently.
    /// </summary>
    [Fact]
    public void TheEventNameIsMatchedWhateverItsCase()
    {
        Assert.True(_mapper.Map(Payload("media_pending"))!.IsAdmin);
        Assert.Equal("alice", _mapper.Map(Payload("Media_Approved"))!.Username);
    }

    /// <summary>
    /// An event added to Seerr after this was written. Its own text is already a
    /// sentence, so it is passed through rather than dropped.
    /// </summary>
    [Fact]
    public void AnUnknownEventKeepsSeerrsOwnText()
    {
        var notification = _mapper.Map(new SeerrWebhookPayload
        {
            NotificationType = "MEDIA_SOMETHING_NEW",
            Subject = "Dune",
            Message = "Something happened",
            Request = new SeerrRequest { RequestedByUsername = "alice" }
        });

        Assert.NotNull(notification);
        Assert.Equal("Dune", notification.Title);
        Assert.Equal("Something happened", notification.Body);
        Assert.Equal("alice", notification.Username);
    }

    /// <summary>
    /// A body reading "requested" with nothing after it is worse than one naming nothing
    /// in particular, so both missing fields have a stand in.
    /// </summary>
    [Fact]
    public void AMissingTitleDoesNotProduceAHalfSentence()
    {
        var notification = _mapper.Map(Payload("MEDIA_PENDING", subject: null));

        Assert.NotNull(notification);
        Assert.Equal("alice requested Unknown media", notification.Body);
    }

    [Fact]
    public void TheRequesterStandsInWhenSeerrNamesNobody()
    {
        var notification = _mapper.Map(Payload("MEDIA_PENDING", requester: null));

        Assert.NotNull(notification);
        Assert.Equal("Someone requested Dune", notification.Body);
    }

    [Fact]
    public void TheMediaNameReachesTheBody()
    {
        Assert.Equal("Dune is available now", _mapper.Map(Payload("MEDIA_AVAILABLE"))!.Body);
        Assert.Equal("Your request for Dune was approved", _mapper.Map(Payload("MEDIA_APPROVED"))!.Body);
    }

    /// <summary>
    /// Only the fields the mapper reads are modelled. Seerr adding one must not fail the
    /// request, and the ones it already sends that are not declared must not be read.
    /// </summary>
    [Fact]
    public void AFieldThisDoesNotModelIsIgnoredRatherThanFatal()
    {
        const string body = """
        {
          "notification_type": "MEDIA_APPROVED",
          "subject": "Dune (2021)",
          "message": "A movie",
          "image": "https://example.invalid/poster.jpg",
          "media": { "media_type": "movie", "tmdbId": "438631" },
          "request": {
            "request_id": "12",
            "requestedBy_username": "alice",
            "requestedBy_email": "alice@example.invalid",
            "requestedBy_settings_discordId": "1234"
          },
          "somethingSeerrAddedLater": { "nested": true }
        }
        """;

        var payload = JsonSerializer.Deserialize<SeerrWebhookPayload>(body);

        Assert.NotNull(payload);
        Assert.Equal("MEDIA_APPROVED", payload.NotificationType);
        Assert.Equal("alice", payload.Request?.RequestedByUsername);

        var notification = _mapper.Map(payload);
        Assert.Equal("alice", notification?.Username);
    }
}
