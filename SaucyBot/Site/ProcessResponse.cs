using Discord;

namespace SaucyBot.Site;

public sealed record ProcessResponse : IAsyncDisposable
{
    private int _disposed;

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var exceptions = new List<Exception>();

        foreach (var file in Files)
        {
            try
            {
                await file.Stream.DisposeAsync();
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException(exceptions);
        }

        GC.SuppressFinalize(this);
    }
}
