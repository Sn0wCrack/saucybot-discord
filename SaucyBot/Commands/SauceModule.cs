using Discord.Interactions;
using Discord.WebSocket;
using SaucyBot.Services;

namespace SaucyBot.Commands;

public abstract class SauceModule : InteractionModuleBase<SocketInteractionContext<SocketInteraction>>
{
    private readonly SiteManager _siteManager;

    public SauceModule(SiteManager siteManager)
    {
        _siteManager = siteManager;
    }

    protected async Task ProcessSauceAsync()
    {
        if (Context.Interaction is not SocketSlashCommand command)
        {
            return;
        }

        await _siteManager.HandleCommand(command);
    }
}
