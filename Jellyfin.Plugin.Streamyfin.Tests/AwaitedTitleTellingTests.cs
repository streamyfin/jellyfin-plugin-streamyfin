using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Streamyfin.Configuration.Notifications;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The awaited title message, sent to Expo the way the server sends it, and what a message
/// that never left does to the record of who was told (#289).
/// </summary>
public sealed class AwaitedTitleTellingTests : IDisposable
{
    private readonly string _directory = TestDirectory.Create();
    private readonly PluginDatabase _db;
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Movie _matrix = new() { Id = Guid.NewGuid(), Name = "The Matrix" };

    /// <summary>
    /// Initializes a new instance of the <see cref="AwaitedTitleTellingTests"/> class.
    /// </summary>
    public AwaitedTitleTellingTests() => _db = new PluginDatabase(_directory);

    private static AwaitedTitle Matrix(Guid userId, string title = "The Matrix") => new()
    {
        UserId = userId,
        MediaType = AwaitedTitles.Movie,
        TmdbId = 603,
        Title = title,
        Year = 1999,
        AddedAt = DateTime.UtcNow
    };

    private void Device(Guid userId) => _db.AddDeviceToken(new DeviceToken
    {
        Token = $"ExponentPushToken[{userId:N}]",
        DeviceId = Guid.NewGuid(),
        UserId = userId,
        Language = "en"
    });

    private static AwaitedTitleAnnouncer Announcer(Expo expo) => new(
        null!,
        null!,
        new NotificationHelper(null, null, new SerializationHelper(), new Factory(expo)),
        new LocalizationHelper(null, null),
        NullLogger<AwaitedTitleAnnouncer>.Instance);

    // Who waits for the movie and was told, or is held: the people its new item message leaves out.
    private HashSet<Guid> LeftOut() =>
        _db.WhoWaitedFor(AwaitedTitles.Movie, 603, null, _matrix.Id, DateTime.MinValue, DateTime.MinValue);

    private List<AwaitedTitle> Arrive() =>
        _db.SettleArrival(AwaitedTitles.Movie, 603, null, _matrix.Id, _ => AwaitedOutcome.Announce);

    /// <summary>
    /// A message Expo took leaves the person told: nothing to tell them again.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task AMessageExpoTookLeavesThePersonTold()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        Device(_alice);
        var expo = new Expo(_ => Ok());

        await Announcer(expo).Announce(_db, _matrix, Arrive());

        Assert.Equal(1, expo.Calls);
        Assert.Empty(_db.SettleHeld(_ => AwaitedOutcome.Announce));
        Assert.Equal(_alice, Assert.Single(LeftOut()));
    }

    /// <summary>
    /// A message Expo refused puts the arrival back as held: the task tells them at its next
    /// run, and they stay left out of the new item message meanwhile.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task AMessageExpoRefusedIsToldAgainLater()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        Device(_alice);

        await Announcer(new Expo(_ => new HttpResponseMessage(HttpStatusCode.BadRequest))).Announce(_db, _matrix, Arrive());

        Assert.Equal(_alice, Assert.Single(LeftOut()));
        Assert.Equal(_alice, Assert.Single(_db.SettleHeld(_ => AwaitedOutcome.Announce)).UserId);
    }

    /// <summary>
    /// A message that never reached Expo, the network down or Expo silent until the client
    /// gave up, is told again later the same way, and does not fail the arrival.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task AMessageThatNeverReachedExpoIsToldAgainLater()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        Device(_alice);

        await Announcer(new Expo(_ => throw new HttpRequestException("no route to exp.host"))).Announce(_db, _matrix, Arrive());

        Assert.Equal(_alice, Assert.Single(_db.SettleHeld(_ => AwaitedOutcome.Announce)).UserId);
    }

    /// <summary>
    /// Somebody without a device has nothing to send to, now or later, so they stay told
    /// rather than being tried again at every run.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SomebodyWithoutADeviceStaysTold()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        var expo = new Expo(_ => Ok());

        await Announcer(expo).Announce(_db, _matrix, Arrive());

        Assert.Equal(0, expo.Calls);
        Assert.Empty(_db.SettleHeld(_ => AwaitedOutcome.Announce));
    }

    /// <summary>
    /// The people who asked for the title by another name get a message of their own, so one
    /// that fails holds only its own people again, and the next still goes.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task OneMessageFailingHoldsOnlyItsOwnPeople()
    {
        _db.AddAwaitedTitle(Matrix(_alice));
        _db.AddAwaitedTitle(Matrix(_bob, "Matrix"));
        Device(_alice);
        Device(_bob);
        var bobs = $"ExponentPushToken[{_bob:N}]";
        var expo = new Expo(body => body.Contains(bobs, StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            : Ok());

        await Announcer(expo).Announce(_db, _matrix, Arrive());

        Assert.Equal(2, expo.Calls);
        Assert.Equal(_bob, Assert.Single(_db.SettleHeld(_ => AwaitedOutcome.Announce)).UserId);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"data":[{"status":"ok","id":"1"}]}""", System.Text.Encoding.UTF8, "application/json")
    };

    /// <inheritdoc />
    public void Dispose() => TestDirectory.Delete(_directory);

    // Answers each request with what it is given the request's body to decide.
    private sealed class Expo(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return answer(body);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
