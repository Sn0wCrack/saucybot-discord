namespace SaucyBot.Library.Sites.Pixiv;

public interface IPixivClient
{
    public Task<bool> Login();
    
    public Task<IllustrationDetailsResponse?> IllustrationDetails(string id);

    public Task<IllustrationPagesResponse?> IllustrationPages(string id);

    public Task<UgoiraMetadataResponse?> UgoiraMetadata(string id);

    public Task<HttpResponseMessage> PokeFile(string url);

    public Task<MemoryStream> GetFile(string url);
}
