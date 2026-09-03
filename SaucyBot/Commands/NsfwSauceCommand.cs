using Discord;
using Discord.Interactions;
using SaucyBot.Services;

namespace SaucyBot.Commands;

public class NsfwSauceCommand : SauceModule, IConditionallyRegisteredModule
{
    public NsfwSauceCommand(SiteManager siteManager) : base(siteManager) { }

    public bool ShouldRegister(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>().GetValue<bool?>("Bot:RestrictNSFW") ?? false;

    [SlashCommand("sauce", "Create an embed from the provided URL")]
    [IntegrationType(ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall)]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel)]
    [NsfwCommand(true)]
    public async Task SauceAsync([Summary("url", "The URL to create the embed from")] string url)
    {
        await ProcessSauceAsync();
    }
}
