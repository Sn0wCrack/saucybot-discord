using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using SaucyBot.Database;
using SaucyBot.Database.Models;
using SaucyBot.Extensions.Database;

namespace SaucyBot.Services;

public sealed class GuildConfigurationManager : IGuildConfigurationManager
{
    private readonly DatabaseContext _context;
    private readonly ICacheManager _cache;
    private readonly ILogger<GuildConfigurationManager> _logger;

    public GuildConfigurationManager(DatabaseContext context, ICacheManager cache, ILogger<GuildConfigurationManager> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<GuildConfiguration?> GetByChannel(IMessageChannel messageChannel)
    {
        if (messageChannel is not SocketGuildChannel channel)
        {
            return null;
        }

        return await GetByGuildId(channel.Guild.Id);
    }

    public async Task<GuildConfiguration?> GetByGuildId(ulong guildId)
    {
        return await _cache.Remember(
            CacheKey(guildId),
            TimeSpan.FromDays(7),
            async () => await _context.FindOrCreateGuildConfigurationByGuildId(guildId)
        );
    }

    public async Task<bool> UpdateGuildConfiguration(GuildConfiguration configuration)
    {
        var existing = await _context.GuildConfigurations
            .Include(gc => gc.RestrictedRoles)
            .FirstOrDefaultAsync(gc => gc.Id == configuration.Id);

        if (existing is null)
        {
            return false;
        }

        existing.MaximumEmbeds = configuration.MaximumEmbeds;
        existing.MaximumPixivImages = configuration.MaximumPixivImages;
        existing.MaximumArtStationImages = configuration.MaximumArtStationImages;
        existing.SendMatchedMessage = configuration.SendMatchedMessage;
        existing.RestrictToRoles = configuration.RestrictToRoles;
        existing.UpdatedAt = DateTime.UtcNow;

        var allowedRoles = configuration.RestrictedRoles.Select(role => new GuildConfigurationRestrictedRole
        {
            GuildConfigurationId = existing.Id,
            RoleId = role.RoleId,
        });

        existing.RestrictedRoles.Sync(
            incomingCollection: allowedRoles,
            currentKeySelector: entity => entity.RoleId,
            incomingKeySelector: dto => dto.RoleId,
            updateAction: (entity, dto) =>
            {
                entity.GuildConfigurationId = dto.GuildConfigurationId;
                entity.RoleId = dto.RoleId;
                entity.UpdatedAt = DateTime.UtcNow;
            },
            createAction: dto => new GuildConfigurationRestrictedRole
            {
                GuildConfigurationId = dto.GuildConfigurationId,
                RoleId = dto.RoleId,
            },
            context: _context
        );

        await _context.SaveChangesAsync();

        await _cache.Set(CacheKey(existing), existing, TimeSpan.FromDays(7));

        return true;
    }

    private static string CacheKey(ulong guildId) => $"database.guild_configuration_{guildId}";

    private static string CacheKey(GuildConfiguration configuration) => CacheKey(configuration.GuildId);

}
