using SaucyBot.Database.Models;
using SaucyBot.Services;

namespace SaucyBot.Commands;

using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

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

        var configuration = await _configurationManager.GetByGuildId(Context.Guild.Id);

        if (configuration is null)
        {
            await RespondAsync("❌ Failed to fetch existing server configuration.", ephemeral: true);
            return;
        }

        var restrictedRoles = SettingsModal.ResolveRestrictedRoles(
            configuration.RestrictedRoles,
            Context.Guild.GetRole);

        var modal = new SettingsModal(configuration, restrictedRoles);

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

        var configuration = await _configurationManager.GetByGuildId(Context.Guild.Id);

        if (configuration is null)
        {
            await RespondAsync("❌ Failed to fetch existing server configuration.", ephemeral: true);
            return;
        }

        configuration.RestrictToRoles = form.ShouldRestrictToRoles;

        var allowedRoles = form.RestrictedRoles.Select(role => new GuildConfigurationRestrictedRole
        {
            GuildConfigurationId = configuration.Id,
            RoleId = role.Id,
        });

        configuration.RestrictedRoles = allowedRoles.ToList();

        await _configurationManager.UpdateGuildConfiguration(configuration);

        await RespondAsync("✅ Settings updated successfully!", ephemeral: true);
    }
}

// Defining the Modal Form Schema
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

    public SettingsModal(GuildConfiguration configuration, IRole[] restrictedRoles)
    {
        ShouldRestrictToRoles = configuration.RestrictToRoles;

        RestrictedRoles = restrictedRoles;
    }

    public static IRole[] ResolveRestrictedRoles(
        IEnumerable<GuildConfigurationRestrictedRole> roles,
        Func<ulong, IRole?> resolver)
    {
        return roles
            .Select(x => resolver(x.RoleId))
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .Cast<IRole>()
            .ToArray();
    }
}
