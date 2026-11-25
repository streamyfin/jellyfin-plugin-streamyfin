using Jellyfin.Plugin.Streamyfin.Api;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A setting given a value of the wrong type in a Yaml document is refused in words that
/// name it and say what it takes, after where the reader stopped.
/// </summary>
/// <remarks>
/// The refusal was the deserializer's own sentence, which names a CLR type and not the
/// setting, so on a long document the line and column were the only way to the mistake.
/// </remarks>
public class YamlWrongTypeTests
{
    private readonly SerializationHelper _serialization = new();

    [Theory]
    [InlineData("settings:\n  forwardSkipTime:\n    value: [1]\n", "Line 3, column 12: Forward skip time (settings.forwardSkipTime.value) takes a whole number, and this is a list.")]
    [InlineData("settings:\n  forwardSkipTime:\n    value: soon\n", "Line 3, column 12: Forward skip time (settings.forwardSkipTime.value) takes a whole number, and this is 'soon'.")]
    [InlineData("settings:\n  forwardSkipTime:\n    locked: maybe\n    value: 10\n", "Line 3, column 13: Forward skip time (settings.forwardSkipTime.locked) takes true or false, and this is 'maybe'.")]
    [InlineData("settings:\n  forwardSkipTime: 10\n", "Line 2, column 20: Forward skip time (settings.forwardSkipTime) takes a block with locked and value, and this is '10'.")]
    [InlineData("settings:\n  forwardSkipTime:\n    value: 99999999999\n", "Line 3, column 12: Forward skip time (settings.forwardSkipTime.value) takes a whole number from 0 to 60, and this is '99999999999'.")]
    [InlineData("settings:\n  stillWatchingPreset:\n    value: nope\n", "Line 3, column 12: Ask \"Are you still watching?\" (settings.stillWatchingPreset.value) takes one of disabled, short, default, long, veryLong, and this is 'nope'.")]
    [InlineData("settings:\n  mpvVoDriver:\n    value: |\n      fast\n      faster\n", "Line 3, column 12: mpv video output driver (settings.mpvVoDriver.value) takes one of gpu-next, gpu, and this is 'fast faster'.")]
    [InlineData("settings:\n  mpvVoDriver:\n    value: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\U0001F600b\n", "Line 3, column 12: mpv video output driver (settings.mpvVoDriver.value) takes one of gpu-next, gpu, and this is 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa…'.")]
    [InlineData("settings:\n  autoLoginSeerr:\n    value: maybe\n", "Line 3, column 12: Sign in to Seerr automatically (settings.autoLoginSeerr.value) takes true or false, and this is 'maybe'.")]
    public void AWrongTypeNamesTheSettingAndWhatItTakes(string yaml, string expected)
    {
        var (config, problem) = StreamyfinController.ReadYaml(_serialization, yaml);

        Assert.Null(config);
        Assert.Equal(expected, problem);
    }

    /// <summary>
    /// What is not one setting's value or lock keeps the reader's words: a key the settings
    /// do not have, a document the reader cannot parse at all, or a value whose tag the
    /// reader does not know, which is what has to change rather than its type.
    /// </summary>
    [Theory]
    [InlineData("settings:\n  forwardSkipTime:\n    value: !unknown 10\n", "!unknown")]
    [InlineData("settings:\n  forwardSkipTime:\n    valeu: 10\n", "valeu")]
    [InlineData("settings:\n  noSuchSetting:\n    value: 10\n", "noSuchSetting")]
    [InlineData("settings: [unclosed\n", "Line ")]
    public void AnythingElseKeepsTheReadersWords(string yaml, string said)
    {
        var (config, problem) = StreamyfinController.ReadYaml(_serialization, yaml);

        Assert.Null(config);
        Assert.Contains(said, problem);
        Assert.DoesNotContain(" takes ", problem);
    }
}
