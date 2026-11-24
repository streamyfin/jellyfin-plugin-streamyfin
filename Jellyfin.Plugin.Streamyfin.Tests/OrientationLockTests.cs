using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The orientation lock is served to the app as a number, not as a name, because
/// <c>SerializationHelper</c> registers a <c>JsonNumberEnumConverter</c> for it. The app
/// hands that number straight to Expo's <c>ScreenOrientation.lockAsync</c>, so these values
/// are a contract with <c>expo-screen-orientation</c> rather than an internal detail.
/// Renumbering a member silently changes what every device does.
/// </summary>
public class OrientationLockTests
{
    /// <summary>
    /// Every member matches its counterpart in Expo's <c>OrientationLock</c>.
    /// </summary>
    [Theory]
    [InlineData(OrientationLock.Default, 0)]
    [InlineData(OrientationLock.PortraitUp, 3)]
    [InlineData(OrientationLock.Landscape, 5)]
    [InlineData(OrientationLock.LandscapeLeft, 6)]
    [InlineData(OrientationLock.LandscapeRight, 7)]
    public void MemberMatchesExpoScreenOrientation(OrientationLock member, int expected)
    {
        Assert.Equal(expected, (int)member);
    }

    /// <summary>
    /// The value serves as a number over the wire. A name would reach the app as a string
    /// and Expo would not recognise it.
    /// </summary>
    [Fact]
    public void OrientationIsServedAsANumber()
    {
        var helper = new SerializationHelper();

        var json = helper.SerializeToJson(
            new Settings
            {
                defaultVideoOrientation = new Lockable<OrientationLock>
                {
                    locked = true,
                    value = OrientationLock.Landscape
                }
            });

        Assert.Contains("\"value\":5", json.Replace(" ", string.Empty, System.StringComparison.Ordinal), System.StringComparison.Ordinal);
    }
}
