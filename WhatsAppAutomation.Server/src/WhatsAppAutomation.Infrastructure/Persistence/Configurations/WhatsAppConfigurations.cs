using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;
using WhatsAppAutomation.Infrastructure.Identity;

namespace WhatsAppAutomation.Infrastructure.Persistence.Configurations;

internal sealed class WhatsAppAccountConfiguration : IEntityTypeConfiguration<WhatsAppAccount>
{
    public void Configure(EntityTypeBuilder<WhatsAppAccount> builder)
    {
        builder.ToTable("whatsapp_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.StatusReason).HasMaxLength(WhatsAppAccount.ReasonMaxLength);
        builder.Property(a => a.PhoneNumber).HasMaxLength(PhoneNumber.MaxLength);
        builder.Property(a => a.ProfileName).HasMaxLength(WhatsAppAccount.ProfileNameMaxLength);

        // One linked number per user.
        builder.HasIndex(a => a.UserId).IsUnique();

        // One WhatsApp number belongs to one user on the whole platform.
        builder.HasIndex(a => a.PhoneNumber).IsUnique().HasFilter("phone_number IS NOT NULL");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OutgoingMessageConfiguration : IEntityTypeConfiguration<OutgoingMessage>
{
    public void Configure(EntityTypeBuilder<OutgoingMessage> builder)
    {
        builder.ToTable("outgoing_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.ToPhoneNumber).HasMaxLength(PhoneNumber.MaxLength).IsRequired();
        builder.Property(m => m.Text).HasMaxLength(OutgoingMessage.TextMaxLength).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.WhatsAppMessageId).HasMaxLength(OutgoingMessage.WhatsAppIdMaxLength).HasColumnName("whatsapp_message_id");
        builder.Property(m => m.ErrorCode).HasMaxLength(OutgoingMessage.ErrorCodeMaxLength);

        // PostgreSQL's built-in row version (xmin): two updates of the same message at the same
        // time cannot silently overwrite each other.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(m => new { m.UserId, m.CreatedAt });
        builder.HasIndex(m => new { m.AccountId, m.WhatsAppMessageId });
        builder.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(m => m.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// The table the Node.js gateway keeps the saved WhatsApp logins in. The API never reads it: every
/// value is encrypted with a key only the gateway has. It is defined here so that migrations
/// create it, and so that deleting an account deletes its login.
/// </summary>
internal sealed class WhatsAppAuthEntry
{
    public Guid AccountId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class WhatsAppAuthEntryConfiguration : IEntityTypeConfiguration<WhatsAppAuthEntry>
{
    public void Configure(EntityTypeBuilder<WhatsAppAuthEntry> builder)
    {
        builder.ToTable("whatsapp_auth_state");
        builder.HasKey(e => new { e.AccountId, e.Kind, e.KeyId });
        builder.Property(e => e.Kind).HasMaxLength(50);
        builder.Property(e => e.KeyId).HasMaxLength(500);
        builder.Property(e => e.Data).IsRequired();
        builder.HasOne<WhatsAppAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
