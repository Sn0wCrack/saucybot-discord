namespace SaucyBot.Library.Sites.DeviantArt;

public interface IDeviantArtOpenEmbedClient
{
    public Task<OpenEmbedResponse?> Get(string url);
}
