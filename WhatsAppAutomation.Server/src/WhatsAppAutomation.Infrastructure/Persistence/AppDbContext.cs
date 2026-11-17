using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Infrastructure.Identity;

namespace WhatsAppAutomation.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<WhatsAppAccount> WhatsAppAccounts => Set<WhatsAppAccount>();

    public DbSet<OutgoingMessage> OutgoingMessages => Set<OutgoingMessage>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();

    public DbSet<BroadcastRecipient> BroadcastRecipients => Set<BroadcastRecipient>();

    public DbSet<SendingPace> SendingPaces => Set<SendingPace>();

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
        });
        builder.Entity<AppRole>(b =>
        {
            b.ToTable("roles");
            b.Property(r => r.Description).HasMaxLength(SystemRoles.DescriptionMaxLength);
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
