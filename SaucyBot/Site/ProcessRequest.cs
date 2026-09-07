using System.Text.RegularExpressions;
using SaucyBot.Database.Models;

namespace SaucyBot.Site;

public sealed record ProcessRequest
{
    public Match Match { get; }
    public GuildConfiguration? GuildConfiguration { get; }
    public ProcessingContext? Context { get; }

    public bool IsSlashCommand => Context?.Command is not null;

    public bool IsMessage => Context?.Message is not null;

    public string? UserLocale => Context?.Command?.UserLocale;

    public ulong? GuildId => Context?.Message?.GuildId ?? Context?.Command?.GuildId;

    public ProcessRequest(
        Match match,
        GuildConfiguration? guildConfiguration = null,
        ProcessingContext? Context = null
    )
    {
        Match = match;
        GuildConfiguration = guildConfiguration;
        this.Context = Context;
    }

}
