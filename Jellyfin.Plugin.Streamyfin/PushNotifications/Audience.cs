using System.Globalization;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// The devices a message is written for: what language they asked for, and the address
/// they reach the server at.
/// </summary>
/// <param name="Culture">The language to write in, or <c>null</c> for the server's.</param>
/// <param name="ServerUrl">
/// Where those devices reach the server, or <c>null</c> when they did not say, which is
/// what leaves a notification without its poster.
/// </param>
/// <remarks>
/// A message is written once per audience rather than once per device: a server with fifty
/// phones, two languages and one address writes two messages.
/// </remarks>
public sealed record Audience(CultureInfo? Culture, string? ServerUrl);
