using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What a home section is, which used to be whichever of four fields the app
/// happened to test first.
/// </summary>
public class SectionKindTests
{
    /// <summary>
    /// A section written before the kind existed is read by what it carries.
    /// </summary>
    /// <param name="carried">Which payload the section has.</param>
    /// <param name="expected">The kind that implies.</param>
    [Theory]
    [InlineData(SectionKind.items, SectionKind.items)]
    [InlineData(SectionKind.nextUp, SectionKind.nextUp)]
    [InlineData(SectionKind.latest, SectionKind.latest)]
    [InlineData(SectionKind.custom, SectionKind.custom)]
    public void ASectionWithoutAKindIsReadByWhatItCarries(SectionKind carried, SectionKind expected)
    {
        Assert.Equal(expected, Sections.KindOf(Carrying(carried)));
    }

    /// <summary>
    /// A declared kind is the answer, and nothing infers over it.
    /// </summary>
    [Fact]
    public void ADeclaredKindIsTheAnswer()
    {
        var section = Carrying(SectionKind.latest);
        section.kind = SectionKind.latest;

        Assert.Equal(SectionKind.latest, Sections.KindOf(section));
    }

    /// <summary>
    /// Nothing settles the kind of a section carrying nothing, or carrying two.
    /// </summary>
    [Fact]
    public void AnUnsettledSectionHasNoKind()
    {
        Assert.Null(Sections.KindOf(new Section { title = "Empty" }));

        var two = Carrying(SectionKind.items);
        two.latest = new Latest();

        Assert.Null(Sections.KindOf(two));
    }

    /// <summary>
    /// Reading a home layout fills in every kind that was not declared.
    /// </summary>
    /// <remarks>
    /// This is the migration: no configuration is rewritten, and every section the app
    /// is served says what it is.
    /// </remarks>
    [Fact]
    public void DeclaringFillsInEveryKind()
    {
        var home = new Home
        {
            sections =
            [
                Carrying(SectionKind.items),
                Carrying(SectionKind.nextUp),
                Carrying(SectionKind.latest),
                Carrying(SectionKind.custom)
            ]
        };

        Sections.Declare(home);

        Assert.Equal(
            [SectionKind.items, SectionKind.nextUp, SectionKind.latest, SectionKind.custom],
            home.sections!.Select(section => section.kind));
    }

    /// <summary>
    /// Declaring leaves a section nothing settles alone rather than guessing.
    /// </summary>
    [Fact]
    public void DeclaringDoesNotGuess()
    {
        var section = Carrying(SectionKind.items);
        section.nextUp = new NextUp();

        Sections.Declare(new Home { sections = [section] });

        Assert.Null(section.kind);
    }

    /// <summary>
    /// A section carrying two queries is refused, and the message names both.
    /// </summary>
    [Fact]
    public void TwoQueriesAreRefused()
    {
        var section = Carrying(SectionKind.items);
        section.latest = new Latest();
        section.title = "Recently added";

        var problem = Assert.Single(Sections.Problems(new Home { sections = [section] }));

        Assert.Contains("Recently added", problem, System.StringComparison.Ordinal);
        Assert.Contains("items", problem, System.StringComparison.Ordinal);
        Assert.Contains("latest", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A section carrying no query is refused, since it would draw an empty row.
    /// </summary>
    [Fact]
    public void NoQueryIsRefused()
    {
        var problem = Assert.Single(Sections.Problems(new Home
        {
            sections = [new Section { title = "Nothing" }]
        }));

        Assert.Contains("Home section 1", problem, System.StringComparison.Ordinal);
        Assert.Contains("no query", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A kind that disagrees with the query beside it is refused.
    /// </summary>
    [Fact]
    public void AKindThatDisagreesWithItsQueryIsRefused()
    {
        var section = Carrying(SectionKind.items);
        section.kind = SectionKind.nextUp;

        var problem = Assert.Single(Sections.Problems(new Home { sections = [section] }));

        Assert.Contains("nextUp", problem, System.StringComparison.Ordinal);
        Assert.Contains("items", problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A section that is right is not complained about, and neither is no home at all.
    /// </summary>
    [Fact]
    public void NothingIsSaidAboutALayoutThatIsRight()
    {
        Assert.Empty(Sections.Problems((Home?)null));
        Assert.Empty(Sections.Problems(new Home()));
        Assert.Empty(Sections.Problems(new Home { sections = [Carrying(SectionKind.nextUp)] }));
    }

    /// <summary>
    /// The home layout reaches the validation every write path already goes through.
    /// </summary>
    /// <remarks>
    /// The point of the check is the three paths that are not the admin form: the Yaml
    /// tab writes whatever is typed, and the targeting routes take a JSON body.
    /// </remarks>
    [Fact]
    public void TheHomeLayoutIsCheckedWithEverythingElse()
    {
        var section = Carrying(SectionKind.items);
        section.custom = new CustomEndpoint { endpoint = "/anything" };

        var settings = new Settings
        {
            home = new Lockable<Home> { value = new Home { sections = [section] } }
        };

        Assert.Contains("a section is filled by one", SettingsValidation.Message(settings), System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A kind written in the Yaml tab survives the round trip, in both directions.
    /// </summary>
    /// <remarks>
    /// The Yaml tab is where a home layout is edited until P5.3, so the kind has to be
    /// something an administrator can actually type, and it has to come back out of
    /// <c>GET config/yaml</c> the way it went in.
    /// </remarks>
    [Fact]
    public void AKindSurvivesTheYamlRoundTrip()
    {
        var serialization = new SerializationHelper();

        var yaml = serialization.SerializeToYaml(new Configuration.Config
        {
            settings = new Settings
            {
                home = new Lockable<Home>
                {
                    value = new Home { sections = [WithKind(SectionKind.nextUp)] }
                }
            }
        });

        Assert.Contains("kind: nextUp", yaml, System.StringComparison.Ordinal);

        var read = serialization.Deserialize<Configuration.Config>(yaml);

        Assert.Equal(SectionKind.nextUp, read.settings?.home?.value?.sections?[0].kind);
    }

    /// <summary>
    /// A kind written by the app's own serializer comes back as a name, not a number.
    /// </summary>
    [Fact]
    public void AKindReachesTheAppAsAName()
    {
        var json = new SerializationHelper().SerializeToJson(WithKind(SectionKind.latest));

        Assert.Contains("\"latest\"", json, System.StringComparison.Ordinal);
    }

    private static Section WithKind(SectionKind kind)
    {
        var section = Carrying(kind);
        section.kind = kind;
        return section;
    }

    private static Section Carrying(SectionKind kind) => kind switch
    {
        SectionKind.items => new Section { title = "A section", items = new Items() },
        SectionKind.nextUp => new Section { title = "A section", nextUp = new NextUp() },
        SectionKind.latest => new Section { title = "A section", latest = new Latest() },
        _ => new Section { title = "A section", custom = new CustomEndpoint { endpoint = "/x" } }
    };
}
