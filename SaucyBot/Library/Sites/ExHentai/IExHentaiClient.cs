namespace SaucyBot.Library.Sites.ExHentai;

public interface IExHentaiClient
{
    public Task<ExHentaiGalleryPage?> GetGallery(ExHentaiGalleryRequest request);
}
