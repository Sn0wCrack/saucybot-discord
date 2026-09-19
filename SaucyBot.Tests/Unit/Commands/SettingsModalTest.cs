using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using SaucyBot.Commands;
using SaucyBot.Database.Models;
using Xunit;

namespace SaucyBot.Tests.Unit.Commands;

public class SettingsModalTest
{
    private static T? FindComponent<T>(Modal modal) where T : class, IMessageComponent
    {
        foreach (var row in modal.Component.Components)
        {
            if (row is LabelComponent label && label.Component is T control)
            {
                return control;
            }
        }

        return null;
    }

    private static SelectMenuComponent? FindSelectMenu(Modal modal, string customId)
    {
        foreach (var row in modal.Component.Components)
        {
            if (row is LabelComponent label
                && label.Component is SelectMenuComponent menu
                && menu.CustomId == customId)
            {
                return menu;
            }
        }

        return null;
    }

    private static GuildConfiguration Configuration(
        bool restrictToRoles = false,
        IEnumerable<ulong>? restrictedRoleIds = null,
        IEnumerable<string>? disabledSites = null)
    {
        return new GuildConfiguration
        {
            RestrictToRoles = restrictToRoles,
            RestrictedRoles = (restrictedRoleIds ?? [])
                .Select(id => new GuildConfigurationRestrictedRole { RoleId = id })
                .ToList(),
            DisabledSites = (disabledSites ?? [])
                .Select(site => new GuildConfigurationDisabledSite { Site = site })
                .ToList(),
        };
    }

    [Fact]
    public void BuildReturnsSettingsModal()
    {
        var modal = SettingsModal.Build(Configuration(), ["Pixiv"]).Build();

        Assert.Equal("settings_modal", modal.CustomId);
        Assert.Equal("Server Settings", modal.Title);
    }

    [Fact]
    public void BuildSetsRestrictRolesCheckboxFromConfiguration()
    {
        var enabled = SettingsModal.Build(Configuration(restrictToRoles: true), ["Pixiv"]).Build();
        var disabled = SettingsModal.Build(Configuration(restrictToRoles: false), ["Pixiv"]).Build();

        var checkbox = FindComponent<CheckboxComponent>(enabled);
        Assert.NotNull(checkbox);
        Assert.Equal("should_restrict_to_roles", checkbox.CustomId);
        Assert.True(checkbox.DefaultState);
        Assert.False(FindComponent<CheckboxComponent>(disabled)!.DefaultState);
    }

    [Fact]
    public void BuildIncludesRoleSelectWithDefaultsForConfiguredRoles()
    {
        var modal = SettingsModal.Build(Configuration(restrictedRoleIds: [100, 200]), ["Pixiv"]).Build();

        var roleSelect = FindSelectMenu(modal, "restricted_roles");
        Assert.NotNull(roleSelect);
        Assert.Equal(ComponentType.RoleSelect, roleSelect.Type);
        Assert.Equal(0, roleSelect.MinValues);
        Assert.Equal(25, roleSelect.MaxValues);
        Assert.Equal([100UL, 200UL], roleSelect.DefaultValues.Select(value => value.Id).ToArray());
        Assert.All(roleSelect.DefaultValues, value => Assert.Equal(SelectDefaultValueType.Role, value.Type));
    }

    [Fact]
    public void BuildIncludesRoleSelectWithoutDefaultsWhenNoRolesConfigured()
    {
        var modal = SettingsModal.Build(Configuration(), ["Pixiv"]).Build();

        var roleSelect = FindSelectMenu(modal, "restricted_roles");
        Assert.NotNull(roleSelect);
        Assert.Empty(roleSelect.DefaultValues);
    }

    [Fact]
    public void BuildDefaultsDisabledSiteOptionsFromConfiguration()
    {
        var modal = SettingsModal.Build(
            Configuration(disabledSites: ["pixiv"]),
            ["ArtStation", "Pixiv", "fxtwitter"]).Build();

        var siteSelect = FindSelectMenu(modal, "disabled_sites");
        Assert.NotNull(siteSelect);
        Assert.Equal(3, siteSelect.MaxValues);
        Assert.Equal(3, siteSelect.Options.Count);
        Assert.False(siteSelect.Options.Single(option => option.Value == "ArtStation").IsDefault);
        Assert.True(siteSelect.Options.Single(option => option.Value == "Pixiv").IsDefault);
        Assert.False(siteSelect.Options.Single(option => option.Value == "fxtwitter").IsDefault);
    }

    [Fact]
    public void BuildOmitsDisabledSitesSelectWhenNoSitesAreEnabled()
    {
        var modal = SettingsModal.Build(Configuration(), []).Build();

        Assert.Null(FindSelectMenu(modal, "disabled_sites"));
    }

    [Fact]
    public void BuildCapsDisabledSitesSelectWhenMoreSitesThanPlatformAllows()
    {
        var manySites = Enumerable.Range(0, SelectMenuBuilder.MaxOptionCount + 5)
            .Select(index => $"Site {index}")
            .ToArray();

        var modal = SettingsModal.Build(Configuration(), manySites).Build();

        var siteSelect = FindSelectMenu(modal, "disabled_sites");
        Assert.Equal(SelectMenuBuilder.MaxOptionCount, siteSelect!.MaxValues);
        Assert.Equal(SelectMenuBuilder.MaxOptionCount, siteSelect.Options.Count);
    }

    [Fact]
    public void BuildDoesNotEmitEmptyComponentDescriptions()
    {
        var modal = SettingsModal.Build(
            Configuration(restrictedRoleIds: [1], disabledSites: ["pixiv"]),
            ["Pixiv"]).Build();

        var labels = modal.Component.Components.OfType<LabelComponent>().ToArray();

        Assert.NotEmpty(labels);
        Assert.All(labels, label => Assert.Null(label.Description));
    }

    [Fact]
    public void CreateSiteOptionsDefaultsToExistingDisabledSites()
    {
        var options = SettingsModal.CreateSiteOptions(
            ["ArtStation", "Pixiv"],
            ["pixiv"]);

        Assert.Equal(2, options.Count);
        Assert.Equal("ArtStation", options[0].Value);
        Assert.False(options[0].IsDefault);
        Assert.Equal("Pixiv", options[1].Value);
        Assert.True(options[1].IsDefault);
    }

    [Fact]
    public void ResolveDisabledSitesMapsIdentifiersToRecords()
    {
        var id = Guid.NewGuid();
        var sites = SettingsModal.ResolveDisabledSites(id, ["Pixiv", "Pixiv", "fxtwitter"]);

        Assert.Equal(2, sites.Count);
        Assert.Equal(id, sites[0].GuildConfigurationId);
        Assert.Equal("Pixiv", sites[0].Site);
        Assert.Equal("fxtwitter", sites[1].Site);
    }
}
