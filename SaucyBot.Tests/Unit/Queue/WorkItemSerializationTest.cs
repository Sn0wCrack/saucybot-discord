using System;
using SaucyBot.Queue;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class WorkItemSerializationTest
{
    [Fact]
    public void RoundTripPreservesEveryWorkItemField()
    {
        var item = new MessageWorkItem(
            MessageId: 1,
            GuildId: 2,
            ChannelId: 3,
            AuthorId: 4,
            AuthorRoleIds: [5, 6],
            Content: "message",
            ForwardedContent: "forwarded",
            Embeds: [new MessageEmbed("title", "description", "https://example.test")],
            CanCreateEmbed: true,
            CanManageMessages: false,
            CorrelationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        var roundTripped = MessageWorkItem.Deserialize(item.Serialize());

        Assert.Equal(item.MessageId, roundTripped.MessageId);
        Assert.Equal(item.GuildId, roundTripped.GuildId);
        Assert.Equal(item.ChannelId, roundTripped.ChannelId);
        Assert.Equal(item.AuthorId, roundTripped.AuthorId);
        Assert.Equal(item.AuthorRoleIds, roundTripped.AuthorRoleIds);
        Assert.Equal(item.Content, roundTripped.Content);
        Assert.Equal(item.ForwardedContent, roundTripped.ForwardedContent);
        Assert.Equal(item.Embeds, roundTripped.Embeds);
        Assert.Equal(item.CanCreateEmbed, roundTripped.CanCreateEmbed);
        Assert.Equal(item.CanManageMessages, roundTripped.CanManageMessages);
        Assert.Equal(item.CorrelationId, roundTripped.CorrelationId);
    }

    [Fact]
    public void SerializedPayloadContainsItsVersionAndNoSocketObjectTypes()
    {
        var payload = new MessageWorkItem(
            1, 2, 3, 4, [], "message", null, [], false, false, Guid.NewGuid()).Serialize();

        Assert.Contains("\"version\":1", payload);
        Assert.DoesNotContain("Socket", payload);
    }
}
