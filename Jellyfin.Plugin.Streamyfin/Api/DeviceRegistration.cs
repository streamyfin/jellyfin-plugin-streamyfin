using System;
using Jellyfin.Plugin.Streamyfin.Db;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// What the server does with a device registration.
/// </summary>
public enum Registration
{
    /// <summary>The registration is stored.</summary>
    Accepted,

    /// <summary>It names an account other than the one asking.</summary>
    NotYours,

    /// <summary>It carries no push token, so it could receive nothing.</summary>
    NoToken
}

/// <summary>
/// Whose device a registration is, and who may take one away.
/// </summary>
/// <remarks>
/// The routes are authorized and that was all they checked. The account making the request
/// was never compared with the account the body named, so any signed in user could register
/// a device under somebody else and receive what that person receives, or remove a device
/// that was not theirs. A registration also removes the other rows carrying its token now,
/// which would have made the same request a way to take a device away from its owner.
/// </remarks>
public static class DeviceRegistration
{
    /// <summary>
    /// What to do with a registration, and who it ends up belonging to.
    /// </summary>
    /// <param name="registration">
    /// What was posted. When it names no account, the caller's is written into it.
    /// </param>
    /// <param name="callerId">The account making the request, empty for an API key.</param>
    /// <param name="callerIsApiKey">
    /// Whether the request carries an API key. A key is granted by an administrator and
    /// carries no user, so it registers for whoever it names, as it may already send to
    /// anybody.
    /// </param>
    /// <returns>Whether the registration is stored, and why not when it is refused.</returns>
    public static Registration Check(DeviceToken registration, Guid callerId, bool callerIsApiKey)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Token))
        {
            return Registration.NoToken;
        }

        if (callerIsApiKey)
        {
            return Registration.Accepted;
        }

        if (registration.UserId == Guid.Empty)
        {
            registration.UserId = callerId;
            return Registration.Accepted;
        }

        return registration.UserId == callerId ? Registration.Accepted : Registration.NotYours;
    }

    /// <summary>
    /// The account a removal is limited to.
    /// </summary>
    /// <param name="callerId">The account making the request, empty for an API key.</param>
    /// <param name="callerIsApiKey">Whether the request carries an API key.</param>
    /// <returns>
    /// The owner whose device may be removed, or <c>null</c> for a caller that may remove
    /// any of them.
    /// </returns>
    public static Guid? Remover(Guid callerId, bool callerIsApiKey) => callerIsApiKey ? null : callerId;
}
