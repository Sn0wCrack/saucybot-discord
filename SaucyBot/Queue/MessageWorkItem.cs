using System.Text.Json;
using System.Text.Json.Serialization;

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
