using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// Marks an event whose message names an account other than the one being told.
/// </summary>
/// <remarks>
/// "Somebody signed in", "somebody is watching this", "a sign in was refused for this name
/// from this address": each carries a name, and one of them an address. They are for
/// administrators by default, and #29 lets an administrator hand one to anybody, so the
/// page marks these and asks before it hands one to an account that does not administer
/// the server. Here rather than in the page, so that a new event declares what it is
/// once.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AboutSomebodyElseAttribute : Attribute;
