using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Database.Models;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library;
using SaucyBot.Site;

namespace SaucyBot.Services;

public static partial class MessageValidator
{
    [GeneratedRegex(@"(<|\|\|)(?!@|#|:|a:).*(>|\|\|)", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreContentRegex();

    public static ValidationResult ValidateMessage(
        IMessageContext message,
        GuildConfiguration? guildConfiguration)
    {
        if (IgnoreContentRegex().IsMatch(message.AllMessageContent))
        {
            return ValidationResult.Fail("Message contains ignore tags");
        }

        if (!message.CanCreateEmbed)
        {
            return ValidationResult.Fail("Missing channel permissions to create embed");
        }

        if (!UserHasPermissionToEmbed(guildConfiguration, message.AuthorRoleIds))
        {
            return ValidationResult.Fail("User lacks role permission to embed");
        }

        return ValidationResult.Pass();
    }

    public static ValidationResult ValidateCommand(
        ICommandContext command,
        GuildConfiguration? guildConfiguration)
    {
        if (!command.CanCreateEmbed)
        {
            return ValidationResult.Fail("Missing channel permissions to create embed");
        }

        if (!UserHasPermissionToEmbed(guildConfiguration, command.UserRoleIds))
        {
            return ValidationResult.Fail("User lacks role permission to embed");
        }

        return ValidationResult.Pass();
    }

    public static ValidationResult ValidateMessage(SocketUserMessage message, GuildConfiguration? guildConfiguration)
    {
        if (IgnoreContentRegex().IsMatch(message.AllMessageContent()))
        {
            return ValidationResult.Fail("Message contains ignore tags");
        }

        if (!HasPermissionsToCreateEmbed(message))
        {
            return ValidationResult.Fail("Missing channel permissions to create embed");
        }

        if (!UserHasPermissionToEmbed(guildConfiguration, message.Author as SocketGuildUser))
        {
            return ValidationResult.Fail("User lacks role permission to embed");
        }

        return ValidationResult.Pass();
    }

    public static ValidationResult ValidateCommand(
        SocketSlashCommand command,
        GuildConfiguration? guildConfiguration)
    {
        if (!HasPermissionsToCreateEmbed(command))
        {
            return ValidationResult.Fail("Missing channel permissions to create embed");
        }

        if (!UserHasPermissionToEmbed(guildConfiguration, command.User as SocketGuildUser))
        {
            return ValidationResult.Fail("User lacks role permission to embed");
        }

        return ValidationResult.Pass();
    }

    public static bool HasPermissionToHideEmbed(SocketMessage message)
    {
        if (message.Channel is SocketGuildChannel guildChannel)
        {
            var permissions = guildChannel.Guild.CurrentUser.GetPermissions(guildChannel);
            return permissions.Has(ChannelPermission.ManageMessages);
        }

        if (message.Channel is SocketThreadChannel threadChannel)
        {
            var permissions = threadChannel.Guild.CurrentUser.GetPermissions(threadChannel);
            return permissions.Has(ChannelPermission.ManageMessages);
        }

        return false;
    }

    private static bool UserHasPermissionToEmbed(
        GuildConfiguration? guildConfiguration,
        SocketGuildUser? guildUser)
    {
        if (guildConfiguration is null || !guildConfiguration.RestrictToRoles)
        {
            return true;
        }

        if (guildUser is null)
        {
            return true;
        }

        var userRoleIds = guildUser.Roles.Select(x => x.Id);

        return guildConfiguration.RestrictedRoles.Select(x => x.RoleId).Intersect(userRoleIds).Any();
    }

    private static bool UserHasPermissionToEmbed(
        GuildConfiguration? guildConfiguration,
        IReadOnlyCollection<ulong> roleIds)
    {
        if (guildConfiguration is null || !guildConfiguration.RestrictToRoles)
        {
            return true;
        }

        return guildConfiguration.RestrictedRoles.Select(x => x.RoleId).Intersect(roleIds).Any();
    }

    private static bool HasPermissionsToCreateEmbed(SocketMessage message)
    {
        return message.Channel switch
        {
            SocketThreadChannel threadChannel =>
                threadChannel.Guild.CurrentUser.GetPermissions(threadChannel)
                    .Has(Constants.RequiredThreadPermissions),
            SocketGuildChannel guildChannel =>
                guildChannel.Guild.CurrentUser.GetPermissions(guildChannel)
                    .Has(Constants.RequiredChannelPermissions),
            _ => false,
        };
    }

    private static bool HasPermissionsToCreateEmbed(SocketInteraction interaction)
    {
        return interaction.Channel switch
        {
            SocketDMChannel or SocketGroupChannel => true,
            SocketThreadChannel threadChannel =>
                threadChannel.Guild.CurrentUser.GetPermissions(threadChannel)
                    .Has(Constants.RequiredThreadPermissions),
            SocketGuildChannel guildChannel =>
                guildChannel.Guild.CurrentUser.GetPermissions(guildChannel)
                    .Has(Constants.RequiredChannelPermissions),
            _ => false,
        };
    }
}
