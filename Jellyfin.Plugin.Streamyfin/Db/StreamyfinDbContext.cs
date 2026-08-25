using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// The plugin's own database.
/// </summary>
/// <remarks>
/// Jellyfin registers <c>AddPooledDbContextFactory&lt;JellyfinDbContext&gt;</c> on both
/// 10.11 and 12, so a plugin can inject the server's context, but its migrations
/// belong to the server and no plugin table can be grafted onto it. The plugin
/// therefore owns this file, identically on both targets.
/// </remarks>
public class StreamyfinDbContext : DbContext
{
    private readonly string? _dbPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamyfinDbContext"/> class
    /// against a database file.
    /// </summary>
    /// <param name="dbPath">Full path to the SQLite file.</param>
    public StreamyfinDbContext(string dbPath)
    {
        _dbPath = dbPath;
        DeviceTokens = Set<DeviceToken>();
        ImportMarkers = Set<ImportMarker>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamyfinDbContext"/> class
    /// from options. Used by the design time factory and by tests.
    /// </summary>
    /// <param name="options">The options.</param>
    public StreamyfinDbContext(DbContextOptions<StreamyfinDbContext> options) : base(options)
    {
        _dbPath = null;
        DeviceTokens = Set<DeviceToken>();
        ImportMarkers = Set<ImportMarker>();
    }

    /// <summary>
    /// Gets or sets the Expo push tokens, one per device.
    /// </summary>
    public DbSet<DeviceToken> DeviceTokens { get; set; }

    /// <summary>
    /// Gets or sets the record of one time imports that have already run.
    /// </summary>
    public DbSet<ImportMarker> ImportMarkers { get; set; }

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceToken>(entity =>
        {
            entity.ToTable("DeviceTokens");
            entity.HasKey(t => t.DeviceId);
            entity.Property(t => t.Token).IsRequired();
            entity.Property(t => t.Timestamp).IsRequired();
            entity.HasIndex(t => t.UserId);
        });

        modelBuilder.Entity<ImportMarker>(entity =>
        {
            entity.ToTable("ImportMarkers");
            entity.HasKey(m => m.Name);
        });

        base.OnModelCreating(modelBuilder);
    }
}
