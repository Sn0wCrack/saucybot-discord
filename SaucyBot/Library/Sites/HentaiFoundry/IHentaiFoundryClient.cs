namespace SaucyBot.Library.Sites.HentaiFoundry;

public interface IHentaiFoundryClient
{
    public Task<bool> Agree();

    public Task<HentaiFoundryPicture?> GetPage(string url);
}
