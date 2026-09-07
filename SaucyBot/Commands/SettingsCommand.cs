using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using SaucyBot.Database.Models;
using SaucyBot.Services;

namespace SaucyBot.Commands;

public class SettingsModule : InteractionModuleBase<SocketInteractionContext<SocketInteraction>>
{
    private readonly IGuildConfigurationManager _configurationManager;

    public SettingsModule(IGuildConfigurationManager configurationManager)
    {
        _configurationManager = configurationManager;
    }

    [SlashCommand("settings", "Open the server configuration modal.")]
    [IntegrationType(ApplicationIntegrationType.GuildInstall)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task OpenSettingsModal()
    {
        // Enforce server owner validation
        if (Context.Guild.OwnerId != Context.User.Id)
        {
            await RespondAsync("❌ This command is restricted to the server owner.", ephemeral: true);
            return;
        }

        var guildConfiguration = await _configurationManager.GetByGuildId(Context.Guild.Id);

        if (guildConfiguration is null)
        {
            await RespondAsync("❌ Failed to fetch existing server configuration.", ephemeral: true);
            return;
        }

        var restrictedRoles = SettingsModal.ResolveRestrictedRoles(
            guildConfiguration.RestrictedRoles,
            Context.Guild.GetRole);

        var modal = new SettingsModal(guildConfiguration, restrictedRoles);

        await RespondWithModalAsync("settings_modal", modal);
    }

    [ModalInteraction("settings_modal")]
    public async Task HandleSettingsModal(SettingsModal form)
    {
        // Always re-verify ownership on submission to prevent exploit attempts
        if (Context.Guild.OwnerId != Context.User.Id)
        {
            await RespondAsync("❌ Unauthorized access.", ephemeral: true);
            return;
        }

        var guildConfiguration = await _configurationManager.GetByGuildId(Context.Guild.Id);

        if (guildConfiguration is null)
        {
            await RespondAsync("❌ Failed to fetch existing server configuration.", ephemeral: true);
            return;
        }

        guildConfiguration.RestrictToRoles = form.ShouldRestrictToRoles;

        var allowedRoles = form.RestrictedRoles.Select(role => new GuildConfigurationRestrictedRole
        {
            GuildConfigurationId = guildConfiguration.Id,
            RoleId = role.Id,
        });

        guildConfiguration.RestrictedRoles = allowedRoles.ToList();

        await _configurationManager.UpdateGuildConfiguration(guildConfiguration);

        await RespondAsync("✅ Settings updated successfully!", ephemeral: true);
    }
}

// Defines the modal form schema used by SettingsModule.
public class SettingsModal : IModal
{
    public string Title => "Server Settings";

    [InputLabel("Restrict Roles")]
    [ModalCheckbox("should_restrict_to_roles")]
    public bool ShouldRestrictToRoles { get; set; }

    [InputLabel("Whitelisted Roles")]
    [RequiredInput(false)]
    [ModalRoleSelect("restricted_roles", minValues: 0, maxValues: 25)]
    public IRole[] RestrictedRoles { get; set; } = [];

    public SettingsModal() { }

    public SettingsModal(GuildConfiguration guildConfiguration, IRole[] restrictedRoles)
    {
        ShouldRestrictToRoles = guildConfiguration.RestrictToRoles;
        RestrictedRoles = restrictedRoles;
    }

    public static IRole[] ResolveRestrictedRoles(
        IEnumerable<GuildConfigurationRestrictedRole> roles,
        Func<ulong, IRole?> resolver
    )
    {
        return roles
            .Select(x => resolver(x.RoleId))
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .Cast<IRole>()
            .ToArray();
    }
}
