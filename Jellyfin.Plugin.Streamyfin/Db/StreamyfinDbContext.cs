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
        SettingsGroups = Set<SettingsGroup>();
        SettingsGroupMembers = Set<SettingsGroupMember>();
        UserSettingsOverrides = Set<UserSettingsOverride>();
        GlobalConfigurations = Set<GlobalConfiguration>();
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
        SettingsGroups = Set<SettingsGroup>();
        SettingsGroupMembers = Set<SettingsGroupMember>();
        UserSettingsOverrides = Set<UserSettingsOverride>();
        GlobalConfigurations = Set<GlobalConfiguration>();
    }

    /// <summary>
    /// Gets or sets the Expo push tokens, one per device.
    /// </summary>
    public DbSet<DeviceToken> DeviceTokens { get; set; }

    /// <summary>
    /// Gets or sets the record of one time imports that have already run.
    /// </summary>
    public DbSet<ImportMarker> ImportMarkers { get; set; }

    /// <summary>
    /// Gets or sets the groups an administrator can target settings at.
    /// </summary>
    public DbSet<SettingsGroup> SettingsGroups { get; set; }

    /// <summary>
    /// Gets or sets which users belong to which group.
    /// </summary>
    public DbSet<SettingsGroupMember> SettingsGroupMembers { get; set; }

    /// <summary>
    /// Gets or sets the settings targeted at a single user.
    /// </summary>
    public DbSet<UserSettingsOverride> UserSettingsOverrides { get; set; }

    /// <summary>
    /// Gets or sets the configuration the server declares for everyone. One row.
    /// </summary>
    public DbSet<GlobalConfiguration> GlobalConfigurations { get; set; }

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

        modelBuilder.Entity<SettingsGroup>(entity =>
        {
            entity.ToTable("SettingsGroups");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).IsRequired();
            entity.Property(g => g.SettingsJson).IsRequired();
            // Two groups with the same name are two groups an admin cannot tell apart.
            entity.HasIndex(g => g.Name).IsUnique();
        });

        modelBuilder.Entity<SettingsGroupMember>(entity =>
        {
            entity.ToTable("SettingsGroupMembers");
            entity.HasKey(m => new { m.GroupId, m.UserId });
            entity.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<UserSettingsOverride>(entity =>
        {
            entity.ToTable("UserSettingsOverrides");
            entity.HasKey(o => o.UserId);
            entity.Property(o => o.SettingsJson).IsRequired();
        });

        modelBuilder.Entity<GlobalConfiguration>(entity =>
        {
            entity.ToTable("GlobalConfigurations");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ConfigJson).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
