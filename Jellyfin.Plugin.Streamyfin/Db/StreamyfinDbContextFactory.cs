using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// Builds a context for the EF Core command line tools.
/// </summary>
/// <remarks>
/// Required by <c>dotnet ef migrations add</c>, which cannot construct a context
/// that only knows how to take a runtime file path. The database name here is
/// never used at runtime.
/// </remarks>
public class StreamyfinDbContextFactory : IDesignTimeDbContextFactory<StreamyfinDbContext>
{
    /// <inheritdoc/>
    public StreamyfinDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StreamyfinDbContext>();
        optionsBuilder.UseSqlite("Data Source=streamyfin.db");

        return new StreamyfinDbContext(optionsBuilder.Options);
    }
}
