using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Infrastructure.Identity;

namespace WhatsAppAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Name);
        builder.Property(p => p.Name).HasMaxLength(Permissions.NameMaxLength);
        builder.Property(p => p.Group).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(200).IsRequired();

        // Seed data: the table always matches Permissions.All. A new permission in code shows up
        // as an insert in the next migration.
        builder.HasData(Permissions.All.Select(p => new Permission(p)));
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
    }
}

internal sealed class UserSwitchConfiguration : IEntityTypeConfiguration<UserSwitch>
{
    public void Configure(EntityTypeBuilder<UserSwitch> builder)
    {
        builder.ToTable("user_switches");
        builder.HasKey(s => new { s.UserId, s.Permission });
        builder.Property(s => s.Permission).HasMaxLength(Permissions.NameMaxLength);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(s => s.Permission).OnDelete(DeleteBehavior.Cascade);
    }
}
