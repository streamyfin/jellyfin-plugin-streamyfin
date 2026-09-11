using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
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

        Assert.Equal(IntegrationOutcome.Reachable, health.Outcome);
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

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""")).HealthOf(settings);

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

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""")).HealthOf(settings);

        foreach (var one in health)
        {
            Assert.DoesNotContain("internal.example", one.Detail ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("internal.example", one.Version ?? string.Empty, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A service answering 502 is not reported as healthy.
    /// </summary>
    /// <remarks>
    /// The failure this whole thing exists to catch: a stopped container behind a
    /// reverse proxy, painted green, with the app sent at a tab that opens onto
    /// nothing. Any HTTP answer proves the address is reachable, but a 5xx is something
    /// in front of the service saying the service is not working.
    /// </remarks>
    /// <param name="kind">Which service.</param>
    /// <param name="status">What the proxy answered.</param>
    [Theory]
    [InlineData(IntegrationKind.Marlin, HttpStatusCode.BadGateway)]
    [InlineData(IntegrationKind.Marlin, HttpStatusCode.ServiceUnavailable)]
    [InlineData(IntegrationKind.Streamystats, HttpStatusCode.InternalServerError)]
    [InlineData(IntegrationKind.Seerr, HttpStatusCode.ServiceUnavailable)]
    public async Task AServiceAnsweringWithAServerErrorIsNotHealthy(IntegrationKind kind, HttpStatusCode status)
    {
        var health = await ProbeWith(new Answering(status, "bad gateway")).Probe(kind, "https://inside.example.com");

        // Down, not WrongService: a service that is restarting is not a wrong address,
        // and an administrator told otherwise goes editing an address that was right.
        Assert.Equal(IntegrationOutcome.Down, health.Outcome);
        Assert.Contains("not working", health.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused certificate sends an administrator to the certificate, and an
    /// unresolvable host to their name server.
    /// </summary>
    /// <remarks>
    /// Every transport failure read as "nothing answered at that address", which is the
    /// wrong place to look when something did answer and its certificate was refused.
    /// Internal addresses behind a private authority are exactly what this feature is
    /// for.
    /// </remarks>
    /// <param name="thrown">What the transport threw.</param>
    /// <param name="says">A phrase the answer has to carry.</param>
    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task ATransportFailureSaysWhichKind(Exception thrown, string says)
    {
        var health = await ProbeWith(new Throwing(thrown)).Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.Equal(IntegrationOutcome.Unreachable, health.Outcome);
        Assert.Contains(says, health.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the transport failures and what each one should say.
    /// </summary>
    public static TheoryData<Exception, string> TransportFailures => new()
    {
        { new HttpRequestException("tls", new AuthenticationException("bad cert")), "certificate" },
        { new HttpRequestException("dns", new SocketException((int)SocketError.HostNotFound)), "could not be resolved" },
        { new TaskCanceledException("timed out"), "in time" },
        { new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)), "nothing is listening on that port" },
        { new HttpRequestException("unreachable", new SocketException((int)SocketError.HostUnreachable)), "cannot be reached" },
        { new HttpRequestException("nothing else"), "Nothing answered at that address" },
    };

    /// <summary>
    /// A version longer than a version is cut before it is served.
    /// </summary>
    /// <remarks>
    /// It comes from whatever is at the configured address and the health route hands it
    /// to every signed in account.
    /// </remarks>
    [Fact]
    public async Task AVersionIsBoundedBeforeItIsServed()
    {
        var body = $$"""{"version":"{{new string('v', 4000)}}"}""";

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, body))
            .Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.Equal(IntegrationOutcome.Ok, health.Outcome);
        Assert.True(health.Version!.Length <= 64);
    }

    /// <summary>
    /// A 404 or a 401 still counts as reachable, since neither says the service is
    /// broken.
    /// </summary>
    /// <param name="status">What answered.</param>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.OK)]
    public async Task SomethingServingHttpCountsAsReachable(HttpStatusCode status)
    {
        var health = await ProbeWith(new Answering(status, "x")).Probe(IntegrationKind.Marlin, "https://inside.example.com");

        Assert.Equal(IntegrationOutcome.Reachable, health.Outcome);
    }

    /// <summary>
    /// Something serving HTTP with nothing identifying it is not the same answer as a
    /// service that confirmed what it is.
    /// </summary>
    /// <remarks>
    /// The app branches on the outcome to decide whether to offer a tab. The Jellyfin
    /// address typed into the Marlin field answers 200, and read as confirmed it would
    /// open a Marlin tab onto Jellyfin, which is the failure this route exists to stop.
    /// </remarks>
    [Fact]
    public async Task ReachableIsNotTheSameAnswerAsConfirmed()
    {
        var probe = ProbeWith(new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}"""));

        Assert.Equal(IntegrationOutcome.Ok, (await probe.Probe(IntegrationKind.Seerr, "https://requests.example.com")).Outcome);
        Assert.Equal(IntegrationOutcome.Reachable, (await probe.Probe(IntegrationKind.Marlin, "https://requests.example.com")).Outcome);
    }

    /// <summary>
    /// A Seerr that is restarting is not described as the wrong address.
    /// </summary>
    /// <remarks>
    /// A 503 through a reverse proxy told an administrator they had typed the wrong URL,
    /// which sends them editing a correct one. So does an access layer answering 401.
    /// </remarks>
    /// <param name="status">What answered.</param>
    /// <param name="expected">What that means.</param>
    /// <param name="says">A phrase the message has to carry.</param>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, IntegrationOutcome.Down, "not working")]
    [InlineData(HttpStatusCode.BadGateway, IntegrationOutcome.Down, "not working")]
    [InlineData(HttpStatusCode.Unauthorized, IntegrationOutcome.Reachable, "access layer")]
    [InlineData(HttpStatusCode.Found, IntegrationOutcome.Reachable, "Use the address it redirects to")]
    public async Task SeerrSaysWhichOfThreeThingsWentWrong(HttpStatusCode status, IntegrationOutcome expected, string says)
    {
        var health = await ProbeWith(new Answering(status, "x")).Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.Equal(expected, health.Outcome);
        Assert.Contains(says, health.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A body too large to read is reported as unreadable rather than as the wrong
    /// service.
    /// </summary>
    /// <remarks>
    /// A status document is a few hundred bytes, so one that filled the cap is not a
    /// status document. Calling it the wrong service is a diagnosis of the address,
    /// which is not what went wrong.
    /// </remarks>
    [Fact]
    public async Task ABodyTooLargeToReadIsNotCalledTheWrongService()
    {
        var huge = new string('x', 32 * 1024);

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, huge))
            .Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.Equal(IntegrationOutcome.Reachable, health.Outcome);
        Assert.Contains("more than a status document", health.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A caller that goes away does not leave everyone else told that nothing is
    /// answering.
    /// </summary>
    /// <remarks>
    /// The answer is shared, so a phone backgrounded a second into the request would
    /// otherwise write three cancellations into it and take the Seerr tab away from the
    /// whole server for half a minute.
    /// </remarks>
    [Fact]
    public async Task ACallerWhoWalksAwayDoesNotSpeakForEveryoneElse()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        var probe = ProbeWith(handler);
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://requests.example.com" }
        };

        using var gone = new CancellationTokenSource();
        handler.Hold();

        var walkedAway = probe.HealthOf(settings, gone.Token);
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => walkedAway);

        handler.Release();

        var health = await probe.HealthOf(settings);

        Assert.Equal(IntegrationOutcome.Ok, Find(health, IntegrationKind.Seerr).Outcome);
    }

    /// <summary>
    /// Callers arriving together share one round rather than each opening its own.
    /// </summary>
    [Fact]
    public async Task CallersArrivingTogetherShareOneRound()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        var probe = ProbeWith(handler);
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://requests.example.com" },
            marlinServerUrl = new Lockable<string> { value = "https://marlin.example.com" }
        };

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => probe.HealthOf(settings)));

        // Two configured services, asked once between them however many callers arrived.
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>
    /// An address carrying a query or a fragment is still asked about its status
    /// endpoint rather than having the path glued onto the query.
    /// </summary>
    /// <param name="typed">The address as it was pasted.</param>
    /// <param name="expected">Where the probe should go.</param>
    [Theory]
    [InlineData("https://requests.example.com/?instance=1", "https://requests.example.com/api/v1/status")]
    [InlineData("https://requests.example.com/seerr#top", "https://requests.example.com/seerr/api/v1/status")]
    [InlineData("https://requests.example.com/seerr/", "https://requests.example.com/seerr/api/v1/status")]
    public async Task AnAddressWithMoreThanAHostIsStillAskedCorrectly(string typed, string expected)
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");

        var health = await ProbeWith(handler).Probe(IntegrationKind.Seerr, typed);

        Assert.Equal(expected, handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal(IntegrationOutcome.Ok, health.Outcome);
    }

    /// <summary>
    /// A version that is not a version is not invented.
    /// </summary>
    /// <remarks>
    /// A key that is present but null or a number said nothing, and shipping the word
    /// "unknown" as a version would hand the app something it might compare.
    /// </remarks>
    /// <param name="body">What the service answered.</param>
    [Theory]
    [InlineData("""{"commitTag":null}""")]
    [InlineData("""{"commitTag":7}""")]
    [InlineData("""{"version":""}""")]
    public async Task AVersionThatIsNotOneIsNotInvented(string body)
    {
        var health = await ProbeWith(new Answering(HttpStatusCode.OK, body)).Probe(IntegrationKind.Seerr, "https://example.com");

        Assert.Null(health.Version);
    }

    /// <summary>
    /// A build that reports only a commit tag is still Seerr, and the tag is the
    /// version.
    /// </summary>
    [Fact]
    public async Task ABuildWithOnlyACommitTagIsStillSeerr()
    {
        var health = await ProbeWith(new Answering(HttpStatusCode.OK, """{"commitTag":"v2.1.0-4-gabc"}"""))
            .Probe(IntegrationKind.Seerr, "https://example.com");

        Assert.Equal(IntegrationOutcome.Ok, health.Outcome);
        Assert.Equal("v2.1.0-4-gabc", health.Version);
    }

    /// <summary>
    /// Health is answered from a recent probe rather than reaching the services again.
    /// </summary>
    /// <remarks>
    /// Every signed in account may ask, and each ask reaches three third party services
    /// from the server's own network position. Without this, one account in a loop
    /// points the plugin at the administrator's own services.
    /// </remarks>
    [Fact]
    public async Task HealthIsAnsweredFromARecentProbe()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        var probe = ProbeWith(handler);
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://requests.example.com" }
        };

        await probe.HealthOf(settings);
        var reached = handler.Calls;
        await probe.HealthOf(settings);

        Assert.Equal(reached, handler.Calls);
    }

    /// <summary>
    /// Correcting an address is answered at once rather than after the cache expires.
    /// </summary>
    [Fact]
    public async Task ChangingAnAddressIsAskedAgain()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        var probe = ProbeWith(handler);

        await probe.HealthOf(new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://one.example.com" }
        });
        var reached = handler.Calls;

        await probe.HealthOf(new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://two.example.com" }
        });

        Assert.True(handler.Calls > reached);
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

    /// <summary>
    /// A round where nothing answered is still kept, since that is when the cache
    /// matters most.
    /// </summary>
    /// <remarks>
    /// Three services on a host that is off is three probes held for the whole client
    /// timeout. Dropping that answer would make every caller pay it again, which is the
    /// loop the cache exists to stop.
    /// </remarks>
    [Fact]
    public async Task ARoundWhereNothingAnsweredIsStillKept()
    {
        var handler = new Throwing(new HttpRequestException("no route"));
        var probe = new IntegrationProbe(new OneClient(handler));
        var settings = Configured();

        await probe.HealthOf(settings);
        await probe.HealthOf(settings);

        // One reach for the one configured service, and the second ask read the answer.
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// A probe that hits something nobody thought of is an answer, not a round that
    /// faults.
    /// </summary>
    /// <remarks>
    /// The round is stored and replayed, so one that faults is a 500 for every caller
    /// for the next half minute.
    /// </remarks>
    [Fact]
    public async Task AnUnexpectedFailureIsStillAnAnswer()
    {
        var probe = new IntegrationProbe(new OneClient(new Throwing(new InvalidOperationException("something else"))));

        var health = await probe.HealthOf(Configured());

        Assert.Equal(IntegrationOutcome.Unreachable, Find(health, IntegrationKind.Seerr).Outcome);
    }

    /// <summary>
    /// A caller who goes away gets their cancellation rather than a verdict about the
    /// address.
    /// </summary>
    [Fact]
    public async Task ACancelledProbeIsNotAVerdict()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        handler.Hold();
        var probe = ProbeWith(handler);

        using var gone = new CancellationTokenSource();
        var asking = probe.Probe(IntegrationKind.Seerr, "https://requests.example.com", gone.Token);
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);

        handler.Release();
    }

    /// <summary>
    /// A body that ends exactly on the cap is whole, not cut short.
    /// </summary>
    [Fact]
    public async Task ABodyThatEndsExactlyOnTheCapIsWhole()
    {
        // Exactly the cap: 8 characters of JSON around the padding.
        var exact = "{\"x\":\"" + new string('y', (8 * 1024) - 8) + "\"}";
        Assert.Equal(8 * 1024, exact.Length);

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, exact))
            .Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.Equal(IntegrationOutcome.WrongService, health.Outcome);
    }

    /// <summary>
    /// An address is stored the way it was checked, on every write path.
    /// </summary>
    /// <remarks>
    /// The check trims before parsing, so an address pasted with a space passed it and
    /// was stored with the space. The Yaml tab and the targeting routes have no form to
    /// trim it for them.
    /// </remarks>
    [Fact]
    public void AnAddressIsStoredTheWayItWasChecked()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "  https://requests.example.com  " }
        };

        SettingsValidation.Tidy(settings);

        Assert.Equal("https://requests.example.com", settings.jellyseerrServerUrl!.value);
        Assert.Empty(SettingsValidation.Problems(settings));
    }

    /// <summary>
    /// A stored address cannot make one caller's answer be served for another's
    /// settings.
    /// </summary>
    /// <remarks>
    /// Stored values are not checked again on read, so one written before the address
    /// rule existed can hold anything, including whatever separates the parts of a
    /// cache key.
    /// </remarks>
    [Fact]
    public async Task AStoredAddressCannotForgeAnotherSetsKey()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        var probe = ProbeWith(handler);

        await probe.HealthOf(new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://one.example\nMarlin=https://two.example" }
        });
        var reached = handler.Calls;

        await probe.HealthOf(new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "https://one.example" },
            marlinServerUrl = new Lockable<string> { value = "https://two.example" }
        });

        Assert.True(handler.Calls > reached);
    }

    /// <summary>
    /// A version is cut without splitting a character.
    /// </summary>
    [Fact]
    public async Task AVersionIsNotCutThroughACharacter()
    {
        var body = $$"""{"version":"{{new string('v', 63)}}🎬🎬"}""";

        var health = await ProbeWith(new Answering(HttpStatusCode.OK, body))
            .Probe(IntegrationKind.Seerr, "https://requests.example.com");

        Assert.NotNull(health.Version);
        Assert.False(char.IsHighSurrogate(health.Version![^1]));
    }

    /// <summary>
    /// Trimming and refusing happen together, so a write path cannot do one without the
    /// other.
    /// </summary>
    [Fact]
    public void CheckingTrimsAndRefusesInOneCall()
    {
        var settings = new Settings
        {
            jellyseerrServerUrl = new Lockable<string> { value = "  https://requests.example.com  " }
        };

        Assert.Null(SettingsValidation.Check(settings));
        Assert.Equal("https://requests.example.com", settings.jellyseerrServerUrl!.value);
    }

    /// <summary>
    /// The build of a private service is not something every account may read.
    /// </summary>
    /// <remarks>
    /// A development build reports its full commit tag, and the exact build is the usual
    /// first step in picking a published vulnerability for it.
    /// </remarks>
    [Fact]
    public void AVersionIsNotForEveryone()
    {
        IReadOnlyList<IntegrationHealth> health =
        [
            new(IntegrationKind.Seerr, IntegrationOutcome.Ok, null, "develop-68c5bc8c"),
            new(IntegrationKind.Marlin, IntegrationOutcome.NotConfigured, "Nothing is configured.", null)
        ];

        var quiet = IntegrationProbe.WithoutVersions(health);

        Assert.All(quiet, one => Assert.Null(one.Version));
        Assert.Equal(IntegrationOutcome.Ok, quiet[0].Outcome);
        Assert.Equal("Nothing is configured.", quiet[1].Detail);
    }

    /// <summary>
    /// A setting that is an address without being lockable is still checked and still
    /// trimmed.
    /// </summary>
    /// <remarks>
    /// Both halves read the value one level down, where a Lockable keeps it, so a plain
    /// property was a rule the form applied and the server did not.
    /// </remarks>
    [Fact]
    public void AnAddressThatIsNotLockableIsStillChecked()
    {
        var settings = new Settings { preferedLanguage = new Lockable<string> { value = "fr" } };

        // Nothing declares a plain address today, so this holds the shape of the rule
        // rather than a setting: every descriptor that is an address is read the right
        // way round.
        Assert.All(
            SettingsSchema.Descriptors.Where(descriptor => descriptor.IsWebAddress),
            descriptor => Assert.True(descriptor.IsLockable ? descriptor.Value is not null : descriptor.Value is null));

        Assert.Empty(SettingsValidation.Problems(settings));
    }

    /// <summary>
    /// An answer is not reused past its half minute, whatever the table holds.
    /// </summary>
    /// <remarks>
    /// The sweep only runs once the table has more than a handful of entries, and a
    /// server with one address set has one, so freshness has to be read on the way in
    /// or the first answer is replayed for the life of the process.
    /// </remarks>
    [Fact]
    public async Task AnAnswerIsNotReusedForEver()
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");
        var probe = new IntegrationProbe(new OneClient(handler), null, TimeSpan.Zero);
        var settings = Configured();

        await probe.HealthOf(settings);
        var reached = handler.Calls;
        await probe.HealthOf(settings);

        Assert.True(handler.Calls > reached);
    }

    /// <summary>
    /// Link-local is not an address the server will open.
    /// </summary>
    /// <remarks>
    /// A private address is the normal case, since the server and the service usually
    /// share a network. Nothing a person configures lives on link-local, and it is
    /// where a cloud instance keeps its credentials endpoint.
    /// </remarks>
    /// <param name="url">The address.</param>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.1.1")]
    [InlineData("http://[fe80::1]")]
    public async Task LinkLocalIsNotOpened(string url)
    {
        var handler = new Answering(HttpStatusCode.OK, "{}");

        var health = await ProbeWith(handler).Probe(IntegrationKind.Seerr, url);

        Assert.Equal(IntegrationOutcome.NotAUrl, health.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    /// <summary>
    /// A private address is opened, since that is the whole point of probing from the
    /// server.
    /// </summary>
    /// <param name="url">The address.</param>
    [Theory]
    [InlineData("http://10.0.20.132:5055")]
    [InlineData("http://192.168.1.5:5055")]
    [InlineData("http://127.0.0.1:5055")]
    public async Task APrivateAddressIsOpened(string url)
    {
        var handler = new Answering(HttpStatusCode.OK, """{"version":"2.1.0"}""");

        var health = await ProbeWith(handler).Probe(IntegrationKind.Seerr, url);

        Assert.Equal(IntegrationOutcome.Ok, health.Outcome);
    }

    private static Settings Configured() => new()
    {
        jellyseerrServerUrl = new Lockable<string> { value = "https://requests.example.com" }
    };

    private static IntegrationHealth Find(IReadOnlyList<IntegrationHealth> health, IntegrationKind kind) =>
        health.Single(one => one.Kind == kind);

    private static IntegrationProbe ProbeWith(HttpMessageHandler handler) =>
        new(new OneClient(handler));

    // ProbeAll starts three probes before awaiting any of them, so this is driven
    // concurrently. Calls carries the "never opened a connection" assertion elsewhere in
    // this file, and a non-atomic increment would let the fixture lie about it.
    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _held = new();
        private int _calls;
        private bool _holding;

        public int Calls => Volatile.Read(ref _calls);

        public HttpRequestMessage? LastRequest { get; private set; }

        /// <summary>
        /// Answers nothing until released, so a test can watch what happens while a
        /// round is still in flight.
        /// </summary>
        public void Hold() => _holding = true;

        /// <summary>
        /// Lets the held answers through.
        /// </summary>
        public void Release() => _held.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastRequest = request;

            if (_holding)
            {
                // Through the token, the way a real handler waits, or a cancelled probe
                // never observes its own cancellation.
                await _held.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Throwing(Exception exception) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw exception;
        }
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
