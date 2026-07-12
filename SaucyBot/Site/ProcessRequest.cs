using System.Text.RegularExpressions;
using Discord.WebSocket;

namespace SaucyBot.Site;

public sealed record ProcessRequest(
    Match Match,
    SocketUserMessage? Message = null,
    SocketSlashCommand? Command = null
)
{
    public bool IsSlashCommand => Command is not null;

    public bool IsMessage => Message is not null;

    public string? UserLocale => Command?.UserLocale;

    public SocketGuild? Guild => (Message?.Channel as SocketGuildChannel)?.Guild
        ?? (Command?.Channel as SocketGuildChannel)?.Guild;
}
