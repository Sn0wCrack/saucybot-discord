﻿using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SaucyBot.Library;

namespace SaucyBot.Database.Models;

[Index(nameof(GuildId), IsUnique = true)]
public sealed class GuildConfiguration
{
    [Key]
    public uint Id { get; set; }
    
    public ulong GuildId { get; set; }

    public uint MaximumEmbeds { get; set; } = Constants.DefaultMaximumEmbeds;
    public uint MaximumPixivImages { get; set; } = Constants.DefaultMaximumPixivImages;
    public uint MaximumArtStationImages { get; set; } = Constants.DefaultMaximumArtStationImages;
    public bool SendMatchedMessage { get; set; } = Constants.DefaultSendMatchedMessage;

    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
}
