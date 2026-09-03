using Discord;
using SaucyBot.Database.Models;

namespace SaucyBot.Services;

public interface IGuildConfigurationManager
{
    public Task<GuildConfiguration?> GetByChannel(IMessageChannel messageChannel);

    public Task<GuildConfiguration?> GetByGuildId(ulong guildId);

    public Task<bool> UpdateGuildConfiguration(GuildConfiguration guildConfiguration);
}
