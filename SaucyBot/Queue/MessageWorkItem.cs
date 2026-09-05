using System.Text.Json;
using System.Text.Json.Serialization;
using Discord;
using Discord.WebSocket;

namespace SaucyBot.Queue;

public sealed record MessageEmbed(string? Title, string? Description, string? Url);

public sealed record MessageWorkItem(
    ulong MessageId,
    ulong GuildId,
    ulong ChannelId,
    ulong AuthorId,
    IReadOnlyList<ulong> AuthorRoleIds,
    string Content,
    string? ForwardedContent,
    IReadOnlyList<MessageEmbed> Embeds,
    bool CanCreateEmbed,
    bool CanManageMessages,
    Guid CorrelationId,
    DateTimeOffset EnqueuedAt = default)
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static MessageWorkItem? Create(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage)
        {
            return null;
        }

        var guildChannel = userMessage.Channel as SocketGuildChannel;
        var permissions = guildChannel?.Guild.CurrentUser.GetPermissions(guildChannel);
        string? forwardedContent = null;
        if (userMessage.ForwardedMessages.Count > 0)
        {
            forwardedContent = string.Join('\n', userMessage.ForwardedMessages.Select(forwarded => forwarded.Message.Content ?? ""));
        }

        return new MessageWorkItem(
            userMessage.Id,
            guildChannel?.Guild.Id ?? 0,
            userMessage.Channel.Id,
            userMessage.Author.Id,
            (userMessage.Author as SocketGuildUser)?.Roles.Select(role => role.Id).ToArray() ?? [],
            userMessage.Content ?? "",
            forwardedContent,
            userMessage.Embeds.Select(embed => new MessageEmbed(embed.Title, embed.Description, embed.Url)).ToArray(),
            permissions?.Has(ChannelPermission.EmbedLinks) ?? false,
            permissions?.Has(ChannelPermission.ManageMessages) ?? false,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
    }

    public string Serialize() => JsonSerializer.Serialize(new VersionedPayload(CurrentVersion, this), SerializerOptions);

    public static MessageWorkItem Deserialize(string payload)
    {
        var versioned = JsonSerializer.Deserialize<VersionedPayload>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("The work-item payload was empty.");

        if (versioned.Version != CurrentVersion || versioned.Item is null)
        {
            throw new InvalidOperationException($"Unsupported work-item payload version: {versioned.Version}.");
        }

        return versioned.Item;
    }

    private sealed record VersionedPayload(int Version, MessageWorkItem? Item);
}

public sealed record QueuedMessageWorkItem(string EntryId, MessageWorkItem Item);
