using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The tests that swap Jellyfin's static <c>BaseItem.LibraryManager</c>, which run one at a
/// time: two of them at once each put back what the other had set.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StaticLibrary
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Jellyfin's static library manager";
}
