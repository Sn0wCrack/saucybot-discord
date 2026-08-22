using Discord;

namespace SaucyBot.Site;

public sealed record ProcessResponse
{
    public List<Embed> Embeds;
    public List<FileAttachment> Files;
    public string? Text;
    public MessageComponent? Components;
    public bool IsNsfw;

    public ProcessResponse(
        List<Embed>? embeds = null,
        List<FileAttachment>? files = null,
        string? text = null,
        MessageComponent? components = null,
        bool nsfw = false
    )
    {
        Embeds = embeds ?? [];
        Files = files ?? [];
        Text = text;
        Components = components;
        IsNsfw = nsfw;
    }
}
