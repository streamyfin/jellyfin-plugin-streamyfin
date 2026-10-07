using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace Jellyfin.Plugin.Streamyfin.Configuration;

public class Config
{
  [NotNull]
  public Notifications.Notifications? notifications { get; set; }

  [NotNull]
  public Settings.Settings? settings { get; set; }
  
  [NotNull]
  [JsonPropertyName(name: "other")]
  public Other? Other { get; set; }

  /// <summary>
  /// A copy of this configuration holding other settings, for an answer that must not
  /// change the stored one.
  /// </summary>
  /// <param name="replacement">The settings the copy holds.</param>
  /// <returns>The copy.</returns>
  internal Config With(Settings.Settings? replacement)
  {
    var copy = (Config)MemberwiseClone();
    copy.settings = replacement;
    return copy;
  }
}
