using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;
using WhatsAppAutomation.Infrastructure.Identity;

namespace WhatsAppAutomation.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("stored_files");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.FileName).HasMaxLength(StoredFile.FileNameMaxLength).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(StoredFile.ContentTypeMaxLength).IsRequired();
        builder.HasIndex(f => f.UserId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(MessageTemplate.NameMaxLength).IsRequired();
        builder.Property(t => t.Text).HasMaxLength(MessageTemplate.TextMaxLength).IsRequired();
        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => new { t.TenantId, t.IsShared });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(t => t.AttachmentId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SendingPaceConfiguration : IEntityTypeConfiguration<SendingPace>
{
    public void Configure(EntityTypeBuilder<SendingPace> builder)
    {
        builder.ToTable("sending_paces");
        builder.HasKey(p => p.AccountId);
        builder.Property(p => p.AccountId).ValueGeneratedNever();
        builder.HasOne<WhatsAppAccount>().WithOne().HasForeignKey<SendingPace>(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BroadcastConfiguration : IEntityTypeConfiguration<Broadcast>
{
    public void Configure(EntityTypeBuilder<Broadcast> builder)
    {
        builder.ToTable("broadcasts");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).HasMaxLength(Broadcast.NameMaxLength).IsRequired();
        builder.Property(b => b.Text).HasMaxLength(Broadcast.TextMaxLength);
        builder.HasIndex(b => new { b.UserId, b.Target, b.TargetId });
        builder.HasIndex(b => new { b.UserId, b.Status, b.QueuePosition });
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(b => b.AttachmentId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(b => b.TextEn).HasMaxLength(Broadcast.TextMaxLength);
        builder.Property(b => b.TextHi).HasMaxLength(Broadcast.TextMaxLength);
        builder.Property(b => b.TextGu).HasMaxLength(Broadcast.TextMaxLength);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.PauseReason).HasMaxLength(Broadcast.ReasonMaxLength);
        builder.HasIndex(b => new { b.UserId, b.CreatedAt });
        builder.HasIndex(b => b.Status);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Recipients).WithOne().HasForeignKey(r => r.BroadcastId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BroadcastRecipientConfiguration : IEntityTypeConfiguration<BroadcastRecipient>
{
    public void Configure(EntityTypeBuilder<BroadcastRecipient> builder)
    {
        builder.ToTable("broadcast_recipients");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.PhoneNumber).HasMaxLength(PhoneNumber.MaxLength).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(BroadcastRecipient.NameMaxLength).IsRequired();
        builder.Property(r => r.Language).HasMaxLength(2)
            .HasConversion(l => l == null ? null : ContactLanguages.ToCode(l.Value), s => s == null ? null : ContactLanguages.Parse(s));
        builder.Property(r => r.Text).HasMaxLength(Broadcast.TextMaxLength);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Reason).HasMaxLength(BroadcastRecipient.ReasonMaxLength);

        // The sender's queue: the next pending recipient of a broadcast, in order.
        builder.HasIndex(r => new { r.BroadcastId, r.Status, r.Order });

        // "Got a broadcast in the last 7 days" looks up by number.
        builder.HasIndex(r => new { r.PhoneNumber, r.ProcessedAt });
        builder.HasOne<Contact>().WithMany().HasForeignKey(r => r.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<OutgoingMessage>().WithMany().HasForeignKey(r => r.OutgoingMessageId).OnDelete(DeleteBehavior.SetNull);
    }
}
