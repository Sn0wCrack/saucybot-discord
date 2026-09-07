using NSubstitute;
using SaucyBot.Database.Models;
using SaucyBot.Services;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public sealed class MessageValidatorTest
{
    [Fact]
    public void RejectsMessageContainingIgnoreTags()
    {
        var message = Substitute.For<IMessageContext>();
        message.AllMessageContent.Returns("<spoiler>");
        message.CanCreateEmbed.Returns(true);

        var result = MessageValidator.ValidateMessage(message, null);

        Assert.False(result.Passed);
        Assert.Equal("Message contains ignore tags", result.Reason);
    }

    [Fact]
    public void RejectsMessageWithoutEmbedPermission()
    {
        var message = Substitute.For<IMessageContext>();
        message.AllMessageContent.Returns("https://example.test");
        message.CanCreateEmbed.Returns(false);

        var result = MessageValidator.ValidateMessage(message, null);

        Assert.False(result.Passed);
        Assert.Equal("Missing channel permissions to create embed", result.Reason);
    }

    [Fact]
    public void RejectsMessageWithoutAnAllowedRestrictedRole()
    {
        var message = Substitute.For<IMessageContext>();
        message.AllMessageContent.Returns("https://example.test");
        message.CanCreateEmbed.Returns(true);
        message.AuthorRoleIds.Returns([7UL]);
        var configuration = new GuildConfiguration
        {
            RestrictToRoles = true,
            RestrictedRoles =
            [
                new GuildConfigurationRestrictedRole { RoleId = 8 }
            ]
        };

        var result = MessageValidator.ValidateMessage(message, configuration);

        Assert.False(result.Passed);
        Assert.Equal("User lacks role permission to embed", result.Reason);
    }

    [Fact]
    public void AcceptsCommandWithAnAllowedRestrictedRole()
    {
        var command = Substitute.For<ICommandContext>();
        command.CanCreateEmbed.Returns(true);
        command.UserRoleIds.Returns([8UL]);
        var configuration = new GuildConfiguration
        {
            RestrictToRoles = true,
            RestrictedRoles =
            [
                new GuildConfigurationRestrictedRole { RoleId = 8 }
            ]
        };

        var result = MessageValidator.ValidateCommand(command, configuration);

        Assert.True(result.Passed);
    }
}
