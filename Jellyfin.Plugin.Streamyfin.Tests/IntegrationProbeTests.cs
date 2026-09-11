using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Integrations;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Asking a third party service whether it is there.
/// </summary>
/// <remarks>
/// The server is the only thing that can answer this. An administrator types an address
/// their server reaches and a phone on mobile data never will, saves it, and finds out
/// it was wrong when a user reports an empty tab.
/// </remarks>
public class IntegrationProbeTests
{
    /// <summary>
    /// Seerr's own status endpoint answers with a version, which proves both that
    /// something is there and that it is the right something.
    /// </summary>
    [Fact]
    public async Task SeerrIsIdentifiedByItsStatusEndpoint()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0","commitTag":"v2.1.0"}""");

        var health = await ProbeWith(handler).Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.Equal(IntegrationOutcome.Ok, health.Outcome);
        Assert.Equal("2.1.0", health.Version);
        Assert.Equal(
            "https://requests.example.com/api/v1/status",
            handler.LastRequest?.RequestUri?.ToString());
    }

    /// <summary>
    /// A trailing slash does not become a double one, which would 404 on some proxies.
    /// </summary>
    [Fact]
    public async Task ATrailingSlashIsNotDoubled()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");

        await ProbeWith(handler).Probe(IntegrationKind.Seerr, "https://requests.example.com/");

        Assert.Equal(
            "https://requests.example.com/api/v1/status",
            handler.LastRequest?.RequestUri?.ToString());
    }

    /// <summary>
    /// Something that answers but is not Seerr is named as such, since that is the
    /// mistake an administrator actually makes: the Jellyfin address in the Seerr field.
    /// </summary>
    /// <param name="status">What answered.</param>
    /// <param name="body">What it answered with.</param>
    [Theory]
    [InlineData(HttpStatusCode.OK, "<html>a login page</html>")]
    [InlineData(HttpStatusCode.OK, """{"nothing":"useful"}""")]
    [InlineData(HttpStatusCode.NotFound, "not found")]
    public async Task SomethingThatIsNotSeerrIsNamedAsSuch(HttpStatusCode status, string body)
    {
        var health = await ProbeWith(new Answering(status, body)).Probe(IntegrationKind.Seerr, "https://example.com");

        Assert.Equal(IntegrationOutcome.WrongService, health.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(health.Detail));
    }

    /// <summary>
    /// A service with no endpoint that identifies it claims only what it can: something
    /// answered.
    /// </summary>
    /// <param name="kind">Which service.</param>
    [Theory]
    [InlineData(IntegrationKind.Marlin)]
    [InlineData(IntegrationKind.Streamystats)]
    public async Task AServiceWithNoSignatureClaimsOnlyThatSomethingAnswered(IntegrationKind kind)
    {
        var health = await ProbeWith(new Answering(HttpStatusCode.NotFound, "nope"))
            .Probe(kind, "https://inside.example.com");

        Assert.Equal(IntegrationOutcome.Ok, health.Outcome);
        Assert.Contains("only says the address is reachable", health.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing answering is an answer rather than an exception.
    /// </summary>
    [Fact]
    public async Task NothingAnsweringIsAnAnswer()
    {
        var health = await ProbeWith(new Throwing(new HttpRequestException("no route")))
            .Probe(IntegrationKind.Seerr, "https://gone.example.com");

        Assert.Equal(IntegrationOutcome.Unreachable, health.Outcome);
    }

    /// <summary>
    /// A request that times out is unreachable rather than a crash in the route.
    /// </summary>
    [Fact]
    public async Task ATimeoutIsUnreachable()
    {
        var health = await ProbeWith(new Throwing(new TaskCanceledException("timed out")))
            .Probe(IntegrationKind.Marlin, "https://slow.example.com");

        Assert.Equal(IntegrationOutcome.Unreachable, health.Outcome);
    }

    /// <summary>
    /// Nothing configured is not a failure, it is nothing to do.
    /// </summary>
    /// <param name="url">What is configured.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NothingConfiguredIsNotAFailure(string? url)
    {
        var handler = new Answering(HttpStatusCode.OK, "{}");

        var health = await ProbeWith(handler).Probe(IntegrationKind.Seerr, url);

        Assert.Equal(IntegrationOutcome.NotConfigured, health.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// The server only opens http and https, and never opens anything for an address it
    /// refuses.
    /// </summary>
    /// <param name="url">The address as it was typed.</param>
    /// <remarks>
    /// A <c>file:</c> address would have the server read its own disk and report whether
    /// it succeeded, which is a probe answering a question nobody asked. The route is
    /// elevated, so this is not the only thing standing there, but it is the cheap one.
    /// </remarks>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("requests.example.com")]
    [InlineData("not an address at all")]
    public async Task OnlyAnHttpAddressIsOpened(string url)
    {
        var handler = new Answering(HttpStatusCode.OK, "{}");

        var health = await ProbeWith(handler).Probe(IntegrationKind.Seerr, url);

        Assert.Equal(IntegrationOutcome.NotAUrl, health.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// Every integration is answered for, configured or not, so the app gets a complete
    /// picture rather than a list it has to interpret by absence.
    /// </summary>
    [Fact]
    public async Task EveryIntegrationIsAnsweredFor()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://requests.example.com" }
        };

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""")).ProbeAll(settings);

        Assert.Equal(3, health.Count);
        Assert.Equal(IntegrationOutcome.Ok, Find(health, IntegrationKind.Seerr).Outcome);
        Assert.Equal(IntegrationOutcome.NotConfigured, Find(health, IntegrationKind.Marlin).Outcome);
        Assert.Equal(IntegrationOutcome.NotConfigured, Find(health, IntegrationKind.Streamystats).Outcome);
    }

    /// <summary>
    /// No answer carries an address or a key.
    /// </summary>
    /// <remarks>
    /// The health route is readable by every signed in user, because the app changes
    /// what it offers by it. That a service is down is theirs to know; where it lives is
    /// not, and P1.4 exists because this plugin once served that distinction the wrong
    /// way round.
    /// </remarks>
    [Fact]
    public async Task NoAnswerCarriesAnAddress()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://requests.internal.example" },
            marlinServerUrl = new Lockable<string> { value = "https://marlin.internal.example" }
        };

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""")).ProbeAll(settings);

        foreach (var one in health)
        {
            Assert.DoesNotContain("internal.example", one.Detail ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("internal.example", one.Version ?? string.Empty, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An address that is not one is refused before it is stored, not only when it is
    /// probed.
    /// </summary>
    /// <remarks>
    /// Found on the beta: <c>http://:5055</c> parses as YAML, is not an address, was
    /// stored, and was handed to the app. Whether anything answers there is a different
    /// question and only a probe can ask it; whether it is an address at all is
    /// something the server can say at once.
    /// </remarks>
    /// <param name="address">What an administrator typed.</param>
    [Theory]
    [InlineData("http://:5055")]
    [InlineData("requests.example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("a sentence")]
    public void AnAddressThatIsNotOneIsRefusedBeforeItIsStored(string address)
    {
        var problems = SettingsValidation.Problems(new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = address }
        });

        var problem = Assert.Single(problems);

        Assert.Contains("whole http or https address", problem, StringComparison.Ordinal);
        Assert.Contains(address, problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real address, and no address at all, are both fine.
    /// </summary>
    /// <param name="address">What is stored.</param>
    [Theory]
    [InlineData("https://requests.example.com")]
    [InlineData("http://10.0.0.1:5055/seerr")]
    [InlineData("")]
    [InlineData(null)]
    public void ARealAddressAndNoAddressAreBothFine(string? address)
    {
        Assert.Empty(SettingsValidation.Problems(new Settings
        {
            jellyseerrServerUrl = address is null ? null : new Lockable<string> { value = address }
        }));
    }

    private static IntegrationHealth Find(IReadOnlyList<IntegrationHealth> health, IntegrationKind kind) =>
        health.Single(one => one.Kind == kind);

    private static IntegrationProbe ProbeWith(HttpMessageHandler handler) =>
        new(new OneClient(handler));

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Throwing(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
