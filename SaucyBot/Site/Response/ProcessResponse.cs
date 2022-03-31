using Discord;

namespace SaucyBot.Site.Response;

public class ProcessResponse
{
    public List<Embed> Embeds;
    public List<FileAttachment> Files;
    public string? Text;
    
    public ProcessResponse(List<Embed>? embeds = null, List<FileAttachment>? files = null, string? text = null)
    {
        Embeds = embeds ?? new List<Embed>();
        Files = files ?? new List<FileAttachment>();
        Text = text;
    }
}
