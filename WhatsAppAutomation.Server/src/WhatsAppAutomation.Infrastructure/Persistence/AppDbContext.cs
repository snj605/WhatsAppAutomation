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

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<UserSwitch> UserSwitches => Set<UserSwitch>();

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

    public DbSet<OccasionModule> OccasionModules => Set<OccasionModule>();

    public DbSet<ContactOccasion> ContactOccasions => Set<ContactOccasion>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public Task<Guid?> UserTenantAsync(Guid userId, CancellationToken cancellationToken) =>
        Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TenantId).SingleOrDefaultAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
            b.Property(u => u.Kind).HasMaxLength(20)
                .HasConversion(k => UserKinds.ToCode(k), s => UserKinds.Parse(s))
                .HasDefaultValueSql("'member'");
            b.HasIndex(u => new { u.TenantId, u.Kind });
            b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<AppRole>(b =>
        {
            b.ToTable("roles");
            b.Property(r => r.Description).HasMaxLength(200);
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
