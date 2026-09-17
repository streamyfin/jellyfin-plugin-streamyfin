using System;
using Jellyfin.Plugin.Streamyfin.Api;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Whose device a registration is, and who may take it away.
/// </summary>
/// <remarks>
/// The route is authorized and that was all it checked. The account making the request was
/// never compared with the account the body named, so any signed in user could register a
/// device under somebody else and be sent what that person is sent. Registration also
/// removes the other rows carrying its token now, so the same request could take a device
/// away from whoever holds it.
/// </remarks>
public class DeviceOwnershipTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DeviceToken Posted(Guid userId, string token = "ExponentPushToken[a]") =>
        new() { DeviceId = Guid.NewGuid(), Token = token, UserId = userId };

    /// <summary>
    /// A user registers a device for themselves.
    /// </summary>
    [Fact]
    public void AUserRegistersTheirOwnDevice()
    {
        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(Posted(Alice), Alice, callerIsApiKey: false));
    }

    /// <summary>
    /// A user does not register a device for somebody else.
    /// </summary>
    [Fact]
    public void AUserDoesNotRegisterSomebodyElsesDevice()
    {
        Assert.Equal(Registration.NotYours, DeviceRegistration.Check(Posted(Bob), Alice, callerIsApiKey: false));
    }

    /// <summary>
    /// A registration that names nobody is the caller's own, which is what an app that
    /// leaves the field out means.
    /// </summary>
    [Fact]
    public void ARegistrationNamingNobodyIsTheCallers()
    {
        var registration = Posted(Guid.Empty);

        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(registration, Alice, callerIsApiKey: false));
        Assert.Equal(Alice, registration.UserId);
    }

    /// <summary>
    /// An API key carries no user and is granted by an administrator, so it registers for
    /// whoever it names, the way it may already send to anybody.
    /// </summary>
    [Fact]
    public void AnApiKeyRegistersForWhoeverItNames()
    {
        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(Posted(Bob), Guid.Empty, callerIsApiKey: true));
    }

    /// <summary>
    /// A registration with no token is refused rather than stored. It could receive
    /// nothing, and since a registration removes the other rows carrying its token, an
    /// empty one would take the other empty ones with it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARegistrationWithNoTokenIsRefused(string token)
    {
        Assert.Equal(Registration.NoToken, DeviceRegistration.Check(Posted(Alice, token), Alice, callerIsApiKey: false));
    }

    /// <summary>
    /// The language a device sends is stored the way this server names it, and something
    /// that is not a language is stored as none rather than refusing the registration.
    /// </summary>
    [Fact]
    public void TheLanguageADeviceSendsIsTidiedRatherThanRefused()
    {
        var registration = Posted(Alice);
        registration.Language = "  FR-fr ";

        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(registration, Alice, callerIsApiKey: false));
        Assert.Equal("fr-FR", registration.Language);

        registration.Language = "the user's language";

        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(registration, Alice, callerIsApiKey: false));
        Assert.Null(registration.Language);
    }

    /// <summary>
    /// The address a device sends is stored without its trailing slash, and something that
    /// cannot be fetched from is stored as none.
    /// </summary>
    [Fact]
    public void TheAddressADeviceSendsIsTidiedRatherThanRefused()
    {
        var registration = Posted(Alice);
        registration.ServerUrl = " https://jellyfin.example.com/ ";

        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(registration, Alice, callerIsApiKey: false));
        Assert.Equal("https://jellyfin.example.com", registration.ServerUrl);

        registration.ServerUrl = "not an address";

        Assert.Equal(Registration.Accepted, DeviceRegistration.Check(registration, Alice, callerIsApiKey: false));
        Assert.Null(registration.ServerUrl);
    }

    /// <summary>
    /// A user may only remove their own device, and an API key may remove any.
    /// </summary>
    [Fact]
    public void OnlyTheOwnerRemovesADevice()
    {
        Assert.Equal(Alice, DeviceRegistration.Remover(Alice, callerIsApiKey: false));
        Assert.Null(DeviceRegistration.Remover(Guid.Empty, callerIsApiKey: true));
    }
}
