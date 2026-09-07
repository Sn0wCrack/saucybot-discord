using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NSubstitute;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public sealed class ProcessRequestTest
{
    [Fact]
    public void ExposesProcessingContextAndMessageMetadata()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var message = Substitute.For<IMessageContext>();
        message.GuildId.Returns((ulong?)42);

        var context = new ProcessingContext(
            NsfwAllowed: true,
            Message: message,
            CancellationToken: cancellationToken
        );

        var request = new ProcessRequest(new Regex("https://example.test").Match("https://example.test"), Context: context);

        Assert.True(request.IsMessage);
        Assert.False(request.IsSlashCommand);
        Assert.True(request.Context!.NsfwAllowed);
        Assert.Equal(cancellationToken, request.Context.CancellationToken);
        Assert.Equal((ulong?)42, request.GuildId);
    }

    [Fact]
    public void ExposesCommandLocaleAndClassificationFromContext()
    {
        var command = Substitute.For<ICommandContext>();
        command.UserLocale.Returns("en-GB");

        var request = new ProcessRequest(
            new Regex("https://example.test").Match("https://example.test"),
            Context: new ProcessingContext(false, Command: command, CancellationToken: CancellationToken.None)
        );

        Assert.False(request.IsMessage);
        Assert.True(request.IsSlashCommand);
        Assert.Equal("en-GB", request.UserLocale);
    }

    [Fact]
    public void RequestContractDoesNotDependOnDiscordSocketTypes()
    {
        var discordAssemblyName = typeof(Discord.WebSocket.SocketMessage).Assembly.GetName().Name;

        Assert.DoesNotContain(
            typeof(ProcessRequest).GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType.Assembly.GetName().Name),
            name => name == discordAssemblyName);
        Assert.DoesNotContain(
            typeof(ProcessRequest).GetProperties()
                .Select(property => property.PropertyType.Assembly.GetName().Name),
            name => name == discordAssemblyName);
    }
}
