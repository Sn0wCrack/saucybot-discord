using System;
using System.Collections.Generic;
using Discord;
using NSubstitute;
using SaucyBot.Commands;
using SaucyBot.Database.Models;
using Xunit;

namespace SaucyBot.Tests.Unit.Commands;

public class SettingsModalTest
{
    private static IRole Role(ulong id)
    {
        var role = Substitute.For<IRole>();
        role.Id.Returns(id);
        return role;
    }

    [Fact]
    public void ResolveRestrictedRolesReturnsMatchingRolesInOrder()
    {
        var roles = new List<GuildConfigurationRestrictedRole>
        {
            new() { RoleId = 100 },
            new() { RoleId = 200 },
            new() { RoleId = 300 },
        };

        var resolved = SettingsModal.ResolveRestrictedRoles(
            roles,
            id => id == 100 || id == 300 ? Role(id) : null);

        Assert.Equal(2, resolved.Length);
        Assert.Equal(100UL, resolved[0].Id);
        Assert.Equal(300UL, resolved[1].Id);
    }

    [Fact]
    public void ResolveRestrictedRolesFiltersOutMissingRoles()
    {
        var roles = new List<GuildConfigurationRestrictedRole>
        {
            new() { RoleId = 100 },
            new() { RoleId = 200 },
        };

        var resolved = SettingsModal.ResolveRestrictedRoles(
            roles,
            id => id == 200 ? Role(id) : null);

        var resolvedRole = Assert.Single(resolved);
        Assert.Equal(200UL, resolvedRole.Id);
    }

    [Fact]
    public void ResolveRestrictedRolesReturnsEmptyForNoRoles()
    {
        var resolved = SettingsModal.ResolveRestrictedRoles(
            [],
            _ => throw new InvalidOperationException("resolver should not be called"));

        Assert.Empty(resolved);
    }
}
