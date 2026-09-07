using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Services;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public class MessageManagerTest
{
    [Fact]
    public async Task ProcessResponseWithASingleTextElementShouldReturnASingleMessage()
    {
        var logger = Substitute.For<ILogger<MessageManager>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var processResponse = new ProcessResponse(text: "This is a test");

        var messageManager = new MessageManager(logger, config);

        var messages = await MessageManager.PartitionMessages(processResponse);

        Assert.NotNull(messages);
        Assert.NotEmpty(messages);

        var message = (EmbedMessage)messages.First();

        Assert.Equal("This is a test", message.Content);
        Assert.Empty(message.Embeds);
        Assert.Empty(message.Files);
    }

    [Fact]
    public async Task ContextSendRepliesToTheResolvedOriginalMessage()
    {
        var logger = Substitute.For<ILogger<MessageManager>>();
        var config = new ConfigurationBuilder().Build();
        var context = Substitute.For<IMessageContext>();
        var target = Substitute.For<IUserMessage>();
        context.ResolveMessageAsync(Arg.Any<CancellationToken>()).Returns(target);
        context.IsNsfw.Returns(true);
        context.Content.Returns("original");

        var manager = new MessageManager(logger, config);

        await manager.Send(context, new ProcessResponse(text: "reply"), TestContext.Current.CancellationToken);

        await target.Received(1).ReplyAsync(
            "reply",
            allowedMentions: AllowedMentions.None);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ContextHidePermissionUsesContextPermission(bool canManage, bool expected)
    {
        var context = Substitute.For<IMessageContext>();
        context.CanManageMessages.Returns(canManage);

        Assert.Equal(expected, MessageValidator.HasPermissionToHideEmbed(context));
    }
}
