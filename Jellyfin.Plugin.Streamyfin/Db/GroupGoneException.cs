using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// A group was updated after it was deleted, by another administrator say. An update does not
/// create the group again: it came back with the name and settings of the update and none of
/// its members, and the routes answered 200.
/// </summary>
public sealed class GroupGoneException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GroupGoneException"/> class.
    /// </summary>
    /// <param name="id">The id no group has any more.</param>
    /// <param name="inner">What the database said, when it found the group gone on writing.</param>
    public GroupGoneException(Guid id, Exception? inner = null)
        : base($"No group has the id {id}.", inner)
    {
        Id = id;
    }

    /// <summary>
    /// Gets the id no group has any more.
    /// </summary>
    public Guid Id { get; }
}
