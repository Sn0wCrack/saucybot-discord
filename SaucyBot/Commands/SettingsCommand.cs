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

        var restrictedRoles = SettingsModal.ResolveRestrictedRoles(
            guildConfiguration.RestrictedRoles,
            Context.Guild.GetRole);

        var modal = BuildSettingsModal(guildConfiguration, restrictedRoles);

        await Context.Interaction.RespondWithModalAsync(modal.Build());
    }

    private ModalBuilder BuildSettingsModal(GuildConfiguration guildConfiguration, IRole[] restrictedRoles)
    {
        var disabledSites = guildConfiguration.DisabledSites
            .Select(site => site.Site)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modal = new ModalBuilder()
            .WithCustomId("settings_modal")
            .WithTitle(new SettingsModal().Title)
            .AddCheckBox(
                "should_restrict_to_roles",
                "Restrict Roles",
                guildConfiguration.RestrictToRoles);

        var roleSelect = new SelectMenuBuilder("restricted_roles")
            .WithType(ComponentType.RoleSelect)
            .WithMinValues(0)
            .WithMaxValues(25);

        if (restrictedRoles.Length > 0)
        {
            roleSelect.WithDefaultValues(restrictedRoles.Select(SelectMenuDefaultValue.FromRole).ToArray());
        }

        modal.AddSelectMenu("restricted_roles", roleSelect, "Whitelisted Roles");

        var siteOptions = SettingsModal.CreateSiteOptions(
            _siteRegistry.Sites.Select(site => site.Key),
            disabledSites);

        if (siteOptions.Count > 0)
        {
            modal.AddSelectMenu(
                "disabled_sites",
                new SelectMenuBuilder("disabled_sites")
                    .WithMinValues(0)
                    .WithMaxValues(Math.Min(siteOptions.Count, SettingsModal.MaxDisabledSites))
                    .WithPlaceholder("Select sites to disable in this server")
                    .WithOptions(siteOptions),
                "Disabled Sites");
        }

        return modal;
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

        guildConfiguration.DisabledSites = SettingsModal.ResolveDisabledSites(guildConfiguration.Id, form.DisabledSites);

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

    [InputLabel("Disabled Sites")]
    [RequiredInput(false)]
    [ModalSelectMenu("disabled_sites", minValues: 0, maxValues: MaxDisabledSites)]
    public string[] DisabledSites { get; set; } = [];

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

    public const int MaxDisabledSites = 25;

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
