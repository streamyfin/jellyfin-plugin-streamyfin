using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// How the plugin talks to Expo.
///
/// It used to build a <c>new HttpClient()</c> for every notification, which opens a
/// connection pool per send and never reuses one, and it read the body as a ticket list
/// without looking at the status code, so a rejection came back looking like a delivery
/// with no tickets. Both are what these tests hold in place.
/// </summary>
public class PushNotificationClientTests
{
    private const string ExpoSendUrl = "https://exp.host/--/api/v2/push/send";

    // Immediate rather than the real policy: these tests exercise the retries, and the
    // real one sleeps a second and then two between them.
    private static NotificationHelper HelperFor(StubHandler handler) =>
        new(null, null, new SerializationHelper(), new StubHttpClientFactory(handler), ExpoRetry.Immediate);

    private static ExpoNotificationRequest ANotification() =>
        new() { Title = "A title", Body = "A body", To = ["ExponentPushToken[xxx]"] };

    /// <summary>
    /// The send goes through the client the factory hands out. A client built inline gets
    /// its own connection pool every time, which is the socket exhaustion this avoids, and
    /// it also carries the default hundred second timeout inside an event handler.
    /// </summary>
    [Fact]
    public async Task TheSendGoesThroughTheInjectedClient()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"data":[{"status":"ok","id":"1"}]}""");

        var response = await HelperFor(handler).Send(ANotification());

        Assert.Equal(1, handler.Calls);
        Assert.Equal(new Uri(ExpoSendUrl), handler.LastRequest?.RequestUri);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.NotNull(response);
        Assert.Single(response!.Data);
    }

    /// <summary>
    /// A rejected send is not read as a delivery. Expo answers 429 when it is being asked
    /// too often, and the body is not a ticket list; parsing it anyway produced a response
    /// with an empty ticket list, which every caller here treats as "sent".
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ARejectedSendIsNotReadAsSuccess(HttpStatusCode status)
    {
        var handler = new StubHandler(status, "<html>rate limited</html>");

        var response = await HelperFor(handler).Send(ANotification());

        Assert.Null(response);
    }

    /// <summary>
    /// A rate limited send is made again, and the answer to the last try is the answer.
    /// </summary>
    /// <remarks>
    /// Expo caps a project at six hundred notifications a second. The plugin read the
    /// refusal as a delivery with nothing to report, so the notification was lost with a
    /// line in a log nobody reads. A library that adds fifty episodes at once is exactly
    /// the shape that reaches the cap.
    /// </remarks>
    [Fact]
    public async Task ARateLimitedSendIsMadeAgain()
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, "rate limited")
        {
            Then = [(HttpStatusCode.OK, """{"data":[{"status":"ok","id":"1"}]}""")]
        };

        var response = await HelperFor(handler).Send(ANotification());

        Assert.Equal(2, handler.Calls);
        Assert.Single(response!.Data);
    }

    /// <summary>
    /// What Expo says about a request survives being one batch of several.
    /// </summary>
    /// <remarks>
    /// The notifications route hands this response straight back, so an error dropped
    /// while the batches were combined is an error the caller never sees.
    /// </remarks>
    [Fact]
    public async Task ErrorsSurviveBeingCombined()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"data":[],"errors":[{"code":"PUSH_TOO_MANY_EXPERIENCE_IDS","message":"too many"}]}""");

        var response = await HelperFor(handler).Send(new ExpoNotificationRequest
        {
            Title = "A title",
            To = [.. Enumerable.Range(0, 150).Select(i => $"ExponentPushToken[{i}]")]
        });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, response!.Errors.Count);
        Assert.Equal("PUSH_TOO_MANY_EXPERIENCE_IDS", response.Errors[0].Code);
    }

    /// <summary>
    /// A refusal that cannot pass is not made again.
    /// </summary>
    /// <remarks>
    /// A 400 is the same request being wrong twice. Repeating it reaches no device and
    /// costs two more round trips inside an event handler Jellyfin is waiting on.
    /// </remarks>
    [Fact]
    public async Task ARefusalThatCannotPassIsNotMadeAgain()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, "no");

        Assert.Null(await HelperFor(handler).Send(ANotification()));
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// Retrying gives up rather than going round forever.
    /// </summary>
    [Fact]
    public async Task RetryingGivesUp()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "later");

        Assert.Null(await HelperFor(handler).Send(ANotification()));
        Assert.Equal(ExpoRetry.Immediate.Tries, handler.Calls);
    }

    /// <summary>
    /// More recipients than Expo takes go out in more than one request, and every
    /// recipient goes out exactly once.
    /// </summary>
    /// <remarks>
    /// Expo refuses a body carrying more than a hundred recipients, whole. A server with
    /// more devices than that had its library notifications refused entirely, so nobody
    /// was told rather than everybody.
    /// </remarks>
    [Fact]
    public async Task MoreRecipientsThanExpoTakesGoOutInMoreThanOneRequest()
    {
        var tokens = Enumerable.Range(0, 250).Select(i => $"ExponentPushToken[{i}]").ToList();
        var handler = new StubHandler(HttpStatusCode.OK, """{"data":[]}""");

        await HelperFor(handler).Send(new ExpoNotificationRequest { Title = "A title", To = tokens });

        Assert.Equal(3, handler.Calls);

        var sent = handler.Bodies
            .SelectMany(body => Regex.Matches(body, @"ExponentPushToken\[\d+\]").Select(match => match.Value))
            .ToList();

        Assert.Equal(tokens, sent);
    }

    /// <summary>
    /// The client is asked for by name, so its timeout and headers are configured once at
    /// registration rather than per call site.
    /// </summary>
    [Fact]
    public async Task TheClientIsAskedOfTheFactoryByName()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"data":[]}""");
        var factory = new StubHttpClientFactory(handler);

        await new NotificationHelper(null, null, new SerializationHelper(), factory).Send(ANotification());

        Assert.Equal(NotificationHelper.ExpoClientName, factory.RequestedName);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        /// <summary>
        /// Gets the bodies this was asked to send, in order.
        /// </summary>
        public List<string> Bodies { get; } = [];

        /// <summary>
        /// Gets or sets what to answer after the first call, one entry per later call.
        /// The last entry repeats once it runs out.
        /// </summary>
        public IReadOnlyList<(HttpStatusCode Status, string Body)> Then { get; set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            var answer = Calls == 0 || Then.Count == 0
                ? (status, body)
                : Then[Math.Min(Calls - 1, Then.Count - 1)];

            Calls++;

            return new HttpResponseMessage(answer.Item1)
            {
                Content = new StringContent(answer.Item2, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }
}
