using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Tenant> Tenants { get; }

    DbSet<WhatsAppAccount> WhatsAppAccounts { get; }

    DbSet<OutgoingMessage> OutgoingMessages { get; }

    DbSet<Category> Categories { get; }

    DbSet<ContactGroup> ContactGroups { get; }

    DbSet<Contact> Contacts { get; }

    DbSet<UserPreferences> UserPreferences { get; }

    DbSet<Broadcast> Broadcasts { get; }

    DbSet<BroadcastRecipient> BroadcastRecipients { get; }

    DbSet<SendingPace> SendingPaces { get; }

    DbSet<MessageTemplate> MessageTemplates { get; }

    DbSet<StoredFile> StoredFiles { get; }

    DatabaseFacade Database { get; }

    /// <summary>Change tracking for one entity, e.g. to read it again from the database.</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
