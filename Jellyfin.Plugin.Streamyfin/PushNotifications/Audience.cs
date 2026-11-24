using System.Globalization;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// The devices a message is written for: the language they asked for, the address they reach
/// the server at, and what they can show.
/// </summary>
/// <param name="Culture">The language to write in, or <c>null</c> for the server's.</param>
/// <param name="ServerUrl">
/// Where those devices reach the server, or <c>null</c> when they did not say, which is what
/// leaves a notification without its poster.
/// </param>
/// <param name="Channels">Whether they created our Android channels.</param>
/// <param name="Categories">Whether they declared our notification buttons.</param>
/// <remarks>
/// A message is written once per audience rather than once per device: a server with fifty
/// phones, two languages and one address writes two messages.
/// </remarks>
public sealed record Audience(CultureInfo? Culture, string? ServerUrl, bool Channels = false, bool Categories = false);
