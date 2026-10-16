using DashboardBackEnd.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DashboardBackEnd.Data;

// IdentityDbContext declares the AspNet* tables (users, roles, external logins...).
// Application tables (widget instances, subscriptions...) will be added here as DbSets.
public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<ServiceSubscription> ServiceSubscriptions => Set<ServiceSubscription>();
    public DbSet<WidgetInstance> WidgetInstances => Set<WidgetInstance>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<WidgetInstance>(entity =>
        {
            entity.HasIndex(w => w.UserId);
            entity.Property(w => w.WidgetKey).HasMaxLength(64);
            entity.HasOne(w => w.User)
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ServiceSubscription>(entity =>
        {
            entity.HasIndex(s => new { s.UserId, s.ServiceKey }).IsUnique();
            entity.Property(s => s.ServiceKey).HasMaxLength(64);
            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
