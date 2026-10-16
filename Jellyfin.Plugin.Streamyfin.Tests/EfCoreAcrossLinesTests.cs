using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The build for Jellyfin 10.11 runs on Jellyfin 12 as well: a server moved to 12 keeps it until
/// the next release (#241). It is compiled against EF Core 9 and runs on 12's EF Core 10, so a
/// method that names what EF Core 10 no longer has fails to compile there. The repair of the
/// group names called <c>ExecuteUpdate</c>, whose argument EF Core 10 changed, and on 12 the
/// plugin did not start: Jellyfin marked it Malfunctioned.
/// </summary>
/// <remarks>
/// The build refuses <c>ExecuteUpdate</c> in the plugin, through <c>BannedSymbols.txt</c>
/// (RS0030), in both of its forms, since each build sees only its own EF Core's. Loading the
/// entities and saving the changes compiles to the same calls on both.
/// </remarks>
public class EfCoreAcrossLinesTests
{
    /// <summary>
    /// The banned list names <c>ExecuteUpdate</c> and <c>ExecuteUpdateAsync</c> as EF Core 9 and
    /// EF Core 10 declare them.
    /// </summary>
    /// <param name="symbol">The documentation id of one of them.</param>
    [Theory]
    [InlineData("M:Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdate``1(System.Linq.IQueryable{``0},System.Linq.Expressions.Expression{System.Func{Microsoft.EntityFrameworkCore.Query.SetPropertyCalls{``0},Microsoft.EntityFrameworkCore.Query.SetPropertyCalls{``0}}})")]
    [InlineData("M:Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdateAsync``1(System.Linq.IQueryable{``0},System.Linq.Expressions.Expression{System.Func{Microsoft.EntityFrameworkCore.Query.SetPropertyCalls{``0},Microsoft.EntityFrameworkCore.Query.SetPropertyCalls{``0}}},System.Threading.CancellationToken)")]
    [InlineData("M:Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdate``1(System.Linq.IQueryable{``0},System.Action{Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder{``0}})")]
    [InlineData("M:Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdateAsync``1(System.Linq.IQueryable{``0},System.Action{Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder{``0}},System.Threading.CancellationToken)")]
    public void TheBuildRefusesWhatEfCoreTenChanged(string symbol)
    {
        var banned = File.ReadAllLines(Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin", "BannedSymbols.txt"))
            .Select(line => line.Split(';', 2)[0].Trim());

        Assert.Contains(symbol, banned);
    }
}
