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

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(rp => new { rp.RoleId, rp.Permission });
        builder.Property(rp => rp.Permission).HasMaxLength(Permissions.NameMaxLength);

        builder.HasOne<AppRole>().WithMany().HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(rp => rp.Permission).OnDelete(DeleteBehavior.Cascade);
    }
}
