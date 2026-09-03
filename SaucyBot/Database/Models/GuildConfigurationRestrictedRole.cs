using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaucyBot.Library;

namespace SaucyBot.Database.Models;

public class GuildConfigurationRestrictedRole
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GuildConfigurationId { get; set; }

    public ulong RoleId { get; set; }

    [JsonIgnore]
    public GuildConfiguration GuildConfiguration { get; set; } = null!;

    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class GuildConfigurationRestrictedRoleModelConfiguration : IEntityTypeConfiguration<GuildConfigurationRestrictedRole>
{
    public void Configure(EntityTypeBuilder<GuildConfigurationRestrictedRole> builder)
    {
        builder.HasKey(rr => rr.Id);

        builder.Property(rr => rr.Id)
            .ValueGeneratedNever()
            .HasColumnType("uuid");

        builder.Property(rr => rr.GuildConfigurationId)
            .HasColumnType("uuid");

        builder.HasOne(rr => rr.GuildConfiguration)
            .WithMany(gc => gc.RestrictedRoles)
            .HasForeignKey(rr => rr.GuildConfigurationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rr => rr.GuildConfigurationId);

        builder.HasIndex(rr => new { rr.GuildConfigurationId, rr.RoleId })
            .IsUnique();
    }
}

