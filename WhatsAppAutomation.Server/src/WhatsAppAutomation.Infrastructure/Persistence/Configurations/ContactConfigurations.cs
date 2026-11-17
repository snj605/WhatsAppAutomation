using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;
using WhatsAppAutomation.Infrastructure.Identity;

namespace WhatsAppAutomation.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Category.NameMaxLength).IsRequired();
        builder.Property(c => c.Color).HasMaxLength(Category.ColorMaxLength).IsRequired();
        builder.HasIndex(c => c.UserId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContactGroupConfiguration : IEntityTypeConfiguration<ContactGroup>
{
    public void Configure(EntityTypeBuilder<ContactGroup> builder)
    {
        builder.ToTable("contact_groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(ContactGroup.NameMaxLength).IsRequired();
        builder.HasIndex(g => g.UserId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.PhoneNumber).HasMaxLength(PhoneNumber.MaxLength).IsRequired();
        builder.Property(c => c.SavedName).HasMaxLength(Contact.NameMaxLength);
        builder.Property(c => c.NameEn).HasMaxLength(Contact.NameMaxLength);
        builder.Property(c => c.NameHi).HasMaxLength(Contact.NameMaxLength);
        builder.Property(c => c.NameGu).HasMaxLength(Contact.NameMaxLength);
        builder.Property(c => c.Salutation).HasMaxLength(Contact.SalutationMaxLength);
        builder.Property(c => c.Notes).HasMaxLength(Contact.NotesMaxLength);
        builder.Property(c => c.Language).HasMaxLength(2)
            .HasConversion(l => ContactLanguages.ToCode(l), s => ContactLanguages.Parse(s)!.Value);

        // New contacts are "pending"; contacts saved before the WhatsApp check existed are "unchecked" (the
        // column default). Unchecked is the enum's default value, so EF leaves it to the database on insert.
        builder.Property(c => c.WhatsAppCheck).HasColumnName("whatsapp_check").HasMaxLength(20)
            .HasConversion(v => WhatsAppChecks.ToCode(v), s => WhatsAppChecks.Parse(s))
            .HasDefaultValueSql("'unchecked'");
        builder.Property(c => c.WhatsAppCheckedAt).HasColumnName("whatsapp_checked_at");
        builder.HasIndex(c => new { c.UserId, c.WhatsAppCheck });

        // One contact per number for each user.
        builder.HasIndex(c => new { c.UserId, c.PhoneNumber }).IsUnique();
        builder.HasIndex(c => c.CategoryId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        // A category in use cannot be deleted (checked at the end of the statement, so deleting a user still works).
        builder.HasOne<Category>().WithMany().HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(c => c.Groups).WithMany(g => g.Contacts).UsingEntity(
            "contact_group_members",
            r => r.HasOne(typeof(ContactGroup)).WithMany().HasForeignKey("group_id").OnDelete(DeleteBehavior.Cascade),
            l => l.HasOne(typeof(Contact)).WithMany().HasForeignKey("contact_id").OnDelete(DeleteBehavior.Cascade),
            j => j.HasKey("contact_id", "group_id"));
    }
}

internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("user_preferences");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();
        builder.Property(p => p.DefaultLanguage).HasMaxLength(2)
            .HasConversion(l => ContactLanguages.ToCode(l), s => ContactLanguages.Parse(s)!.Value);
        builder.HasOne<AppUser>().WithOne().HasForeignKey<UserPreferences>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        // Defaults for rows made before sending settings existed (and the same as a new row).
        builder.Property(p => p.DailyLimit).HasDefaultValue(UserPreferences.MinDailyLimit);
        builder.Property(p => p.ChunkSize).HasDefaultValue(50);
        builder.Property(p => p.ChunkRestMinutes).HasDefaultValue(15);
        builder.Property(p => p.SendFrom).HasDefaultValue(new TimeOnly(9, 0));
        builder.Property(p => p.SendUntil).HasDefaultValue(new TimeOnly(21, 0));
        builder.Property(p => p.MinGapSeconds).HasDefaultValue(30);
        builder.Property(p => p.MaxGapSeconds).HasDefaultValue(90);
    }
}

internal sealed class OccasionModuleConfiguration : IEntityTypeConfiguration<OccasionModule>
{
    public void Configure(EntityTypeBuilder<OccasionModule> builder)
    {
        builder.ToTable("occasion_modules");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).HasMaxLength(OccasionModule.NameMaxLength).IsRequired();
        builder.Property(m => m.ForNameLabel).HasMaxLength(OccasionModule.ForNameLabelMaxLength);
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(m => new { m.UserId, m.SortOrder });
        builder.HasIndex(m => m.AutoSend);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);

        // A deleted template only switches the automatic wishes off.
        builder.HasOne<MessageTemplate>().WithMany().HasForeignKey(m => m.TemplateId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ContactOccasionConfiguration : IEntityTypeConfiguration<ContactOccasion>
{
    public void Configure(EntityTypeBuilder<ContactOccasion> builder)
    {
        builder.ToTable("contact_occasions", t =>
        {
            t.HasCheckConstraint("ck_contact_occasions_day", "day BETWEEN 1 AND 31");
            t.HasCheckConstraint("ck_contact_occasions_month", "month BETWEEN 1 AND 12");
        });
        builder.HasKey(o => new { o.ModuleId, o.ContactId });
        builder.Property(o => o.ForName).HasMaxLength(ContactOccasion.ForNameMaxLength);
        builder.HasIndex(o => new { o.ModuleId, o.Month, o.Day });
        builder.HasIndex(o => o.ContactId);
        builder.HasOne<OccasionModule>().WithMany().HasForeignKey(o => o.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Contact>().WithMany().HasForeignKey(o => o.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}
