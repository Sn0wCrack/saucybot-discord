using Discord;

namespace SaucyBot.Site.Response;

public sealed record ProcessResponse
{
    public readonly List<Embed> Embeds;
    public readonly List<FileAttachment> Files;
    public string? Text;
    public MessageComponent? Components;

    public ProcessResponse(List<Embed>? embeds = null, List<FileAttachment>? files = null, string? text = null)
    {
        Embeds = embeds ?? [];
        Files = files ?? [];
        Text = text;
    }
}
