using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SaucyBot.Services;
using SaucyBot.Site.Response;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public class MessageManagerTest
{
    [Fact]
    public async void ProcessResponseWithASingleTextElementShouldReturnASingleMessage()
    {
        var logger = Mock.Of<ILogger<MessageManager>>();
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var processResponse = new ProcessResponse(text: "This is a test");
        
        var messageManager = new MessageManager(logger, config);

        var messages = await messageManager.PartitionMessages(processResponse);
        
        Assert.NotNull(messages);
        Assert.NotEmpty(messages);

        var message = messages.First();
        
        Assert.Equal("This is a test", message.Content);
        Assert.Empty(message.Embeds);
        Assert.Empty(message.Files);
    }
}