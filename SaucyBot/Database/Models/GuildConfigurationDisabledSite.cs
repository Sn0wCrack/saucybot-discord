using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SaucyBot.Database.Models;

public class GuildConfigurationDisabledSite
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GuildConfigurationId { get; set; }

    public string Site { get; set; } = string.Empty;

    [JsonIgnore]
    public GuildConfiguration GuildConfiguration { get; set; } = null!;

    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class GuildConfigurationDisabledSiteModelConfiguration : IEntityTypeConfiguration<GuildConfigurationDisabledSite>
{
    public void Configure(EntityTypeBuilder<GuildConfigurationDisabledSite> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnType("uuid");

        builder.Property(x => x.GuildConfigurationId)
            .HasColumnType("uuid");

        builder.Property(x => x.Site)
            .HasMaxLength(100);

        builder.HasOne(x => x.GuildConfiguration)
            .WithMany(gc => gc.DisabledSites)
            .HasForeignKey(x => x.GuildConfigurationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.GuildConfigurationId);

        builder.HasIndex(x => new { x.GuildConfigurationId, x.Site })
            .IsUnique();
    }
}
