using System;
using System.Net;
using System.Net.Http;
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

    private static NotificationHelper HelperFor(StubHandler handler) =>
        new(null, null, new SerializationHelper(), new StubHttpClientFactory(handler));

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
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
