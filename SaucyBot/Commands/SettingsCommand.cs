using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using SaucyBot.Database.Models;
using SaucyBot.Services;

namespace SaucyBot.Commands;

public class SettingsModule : InteractionModuleBase<SocketInteractionContext<SocketInteraction>>
{
    private readonly IGuildConfigurationManager _configurationManager;
    private readonly SiteRegistry _siteRegistry;

    public SettingsModule(IGuildConfigurationManager configurationManager, SiteRegistry siteRegistry)
    {
        _configurationManager = configurationManager;
        _siteRegistry = siteRegistry;
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

        var modal = SettingsModal.Build(
            guildConfiguration,
            _siteRegistry.Sites.Select(site => site.Key));

        await Context.Interaction.RespondWithModalAsync(modal.Build());
    }

    [ModalInteraction(SettingsModal.ModalId)]
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

        guildConfiguration.DisabledSites = SettingsModal.ResolveDisabledSites(guildConfiguration.Id, form.DisabledSites);

        await _configurationManager.UpdateGuildConfiguration(guildConfiguration);

        await RespondAsync("✅ Settings updated successfully!", ephemeral: true);
    }
}

// Defines the modal form schema used by SettingsModule.
public class SettingsModal : IModal
{
    public const string ModalId = "settings_modal";

    public const string RestrictToRolesId = "should_restrict_to_roles";
    public const string RestrictedRolesId = "restricted_roles";
    public const string DisabledSitesId = "disabled_sites";

    public const string RestrictToRolesLabel = "Restrict Roles";
    public const string RestrictedRolesLabel = "Whitelisted Roles";
    public const string DisabledSitesLabel = "Disabled Sites";

    public string Title => "Server Settings";

    [InputLabel(RestrictToRolesLabel)]
    [ModalCheckbox(RestrictToRolesId)]
    public bool ShouldRestrictToRoles { get; set; }

    [InputLabel(RestrictedRolesLabel)]
    [RequiredInput(false)]
    [ModalRoleSelect(RestrictedRolesId, minValues: 0, maxValues: 25)]
    public IRole[] RestrictedRoles { get; set; } = [];

    [InputLabel(DisabledSitesLabel)]
    [RequiredInput(false)]
    [ModalSelectMenu(DisabledSitesId, minValues: 0, maxValues: SelectMenuBuilder.MaxOptionCount)]
    public string[] DisabledSites { get; set; } = [];

    public SettingsModal() { }

    public static ModalBuilder Build(
        GuildConfiguration guildConfiguration,
        IEnumerable<string> enabledSiteKeys)
    {
        var modal = new ModalBuilder()
            .WithCustomId(ModalId)
            .WithTitle(new SettingsModal().Title)
            .AddCheckBox(
                label: RestrictToRolesLabel,
                customId: RestrictToRolesId,
                defaultState: guildConfiguration.RestrictToRoles);

        var roleSelect = new SelectMenuBuilder(RestrictedRolesId)
            .WithRequired(false)
            .WithType(ComponentType.RoleSelect)
            .WithMinValues(0)
            .WithMaxValues(SelectMenuBuilder.MaxOptionCount);

        var restrictedRoleIds = guildConfiguration.RestrictedRoles
            .Select(role => role.RoleId)
            .Distinct()
            .ToArray();

        if (restrictedRoleIds.Length > 0)
        {
            roleSelect.WithDefaultValues(
                restrictedRoleIds
                    .Select(id => new SelectMenuDefaultValue(id, SelectDefaultValueType.Role))
                    .ToArray());
        }

        modal.AddSelectMenu(label: RestrictedRolesLabel, roleSelect);

        var siteOptions = CreateSiteOptions(
            enabledSiteKeys,
            guildConfiguration.DisabledSites.Select(site => site.Site));

        if (siteOptions.Count > 0)
        {
            modal.AddSelectMenu(
                label: DisabledSitesLabel,
                new SelectMenuBuilder(DisabledSitesId)
                    .WithRequired(false)
                    .WithMinValues(0)
                    .WithMaxValues(Math.Min(siteOptions.Count, SelectMenuBuilder.MaxOptionCount))
                    .WithPlaceholder("Select sites to disable in this server")
                    .WithOptions(siteOptions));
        }

        return modal;
    }

    public static List<SelectMenuOptionBuilder> CreateSiteOptions(
        IEnumerable<string> enabledSites,
        IEnumerable<string> disabledSites)
    {
        var disabled = disabledSites.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return enabledSites
            .Select(site => new SelectMenuOptionBuilder()
                .WithLabel(site)
                .WithValue(site)
                .WithDefault(disabled.Contains(site)))
            .Take(SelectMenuBuilder.MaxOptionCount)
            .ToList();
    }

    public static List<GuildConfigurationDisabledSite> ResolveDisabledSites(
        Guid guildConfigurationId,
        IEnumerable<string> siteIdentifiers)
    {
        return siteIdentifiers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(site => new GuildConfigurationDisabledSite
            {
                GuildConfigurationId = guildConfigurationId,
                Site = site,
            })
            .ToList();
    }
}
