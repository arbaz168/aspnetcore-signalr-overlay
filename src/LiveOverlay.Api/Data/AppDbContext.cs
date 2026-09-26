using LiveOverlay.Api.Channels;
using LiveOverlay.Api.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LiveOverlay.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelEvent> Events => Set<ChannelEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(60);
            entity.Property(c => c.Currency).HasMaxLength(3);
            entity.Property(c => c.DashboardKeyHash).HasMaxLength(64);
            entity.Property(c => c.OverlayTokenHash).HasMaxLength(64);
            entity.Property(c => c.GoalTitle).HasMaxLength(60);
            entity.HasIndex(c => c.DashboardKeyHash).IsUnique();
            entity.HasIndex(c => c.OverlayTokenHash).IsUnique();
        });

        modelBuilder.Entity<ChannelEvent>(entity =>
        {
            entity.ToTable("Events");

            // Backstops for the two guarantees. The publisher never relies on them in the normal path,
            // but a bug there fails loudly instead of producing a duplicate or a repeated sequence number.
            entity.HasIndex(e => new { e.ChannelId, e.Sequence }).IsUnique();
            entity.HasIndex(e => new { e.ChannelId, e.IdempotencyKey }).IsUnique();

            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DisplayName).HasMaxLength(60);
            entity.Property(e => e.Message).HasMaxLength(200);
            entity.Property(e => e.GoalTitle).HasMaxLength(60);
            entity.HasOne<Channel>().WithMany().HasForeignKey(e => e.ChannelId);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
