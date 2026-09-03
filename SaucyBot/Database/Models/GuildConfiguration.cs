using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaucyBot.Library;

namespace SaucyBot.Database.Models;

public sealed class GuildConfiguration
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public ulong GuildId { get; set; }

    public uint MaximumEmbeds { get; set; } = Constants.DefaultMaximumEmbeds;
    public uint MaximumPixivImages { get; set; } = Constants.DefaultMaximumPixivImages;
    public uint MaximumArtStationImages { get; set; } = Constants.DefaultMaximumArtStationImages;
    public bool SendMatchedMessage { get; set; } = Constants.DefaultSendMatchedMessage;
    public bool RestrictToRoles { get; set; } = false;

    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;


    public ICollection<GuildConfigurationRestrictedRole> RestrictedRoles { get; set; } = new List<GuildConfigurationRestrictedRole>();
}

public class GuildConfigurationModelConfiguration : IEntityTypeConfiguration<GuildConfiguration>
{
    public void Configure(EntityTypeBuilder<GuildConfiguration> builder)
    {
        builder.HasKey(gc => gc.Id);

        builder.Property(gc => gc.Id)
            .ValueGeneratedNever()
            .HasColumnType("uuid");

        builder.HasIndex(gc => gc.GuildId)
            .IsUnique();

        builder
            .Property(gc => gc.RestrictToRoles)
            .HasDefaultValue(false);
    }
}
