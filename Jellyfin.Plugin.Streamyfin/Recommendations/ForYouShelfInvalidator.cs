using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Streamyfin.Recommendations;

/// <summary>
/// Throws somebody's "for you" row away when they start watching something.
/// </summary>
/// <remarks>
/// A shelf stands for ten minutes, which is right for scrolling and wrong for the moment
/// somebody presses play: what they just started is the strongest thing there is to go on,
/// and the row would otherwise carry on recommending things around what they watched
/// yesterday. Playback rather than playback finished, since a row built from what somebody
/// is in the middle of is already better than one built without it.
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
            shelves.Forget(user.Id);
        }

        return Task.CompletedTask;
    }
}
