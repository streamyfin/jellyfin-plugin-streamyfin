using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Streamyfin.Recommendations;

/// <summary>
/// Throws somebody's "for you" row away when they start watching something, and remembers
/// what they started.
/// </summary>
/// <remarks>
/// A shelf stands for ten minutes, which is right for scrolling and wrong for the moment
/// somebody presses play: what they just started is the strongest thing there is to go on,
/// and the row would otherwise carry on recommending things around what they watched
/// yesterday. Playback rather than playback finished, since a row built from what somebody
/// is in the middle of is already better than one built without it.
///
/// Throwing the shelf away is not enough by itself: Jellyfin does not call an item played
/// until it ends, so the queries the next shelf is built from would not mention what was
/// just started. It is handed over as well, and the next shelf is built partly out of it.
/// </remarks>
public class ForYouShelfInvalidator(ForYouShelves shelves) : IEventConsumer<PlaybackStartEventArgs>
{
    /// <inheritdoc />
    public Task OnEvent(PlaybackStartEventArgs? eventArgs)
    {
        if (eventArgs?.Users is null)
        {
            return Task.CompletedTask;
        }

        foreach (var user in eventArgs.Users)
        {
            if (eventArgs.Item is { } item)
            {
                shelves.Started(user.Id, item.Id);
            }

            shelves.Forget(user.Id);
        }

        return Task.CompletedTask;
    }
}
