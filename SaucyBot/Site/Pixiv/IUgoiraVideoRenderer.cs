namespace SaucyBot.Site.Pixiv;

public interface IUgoiraVideoRenderer
{
    Task RenderAsync(string concatFile, string videoFile, CancellationToken cancellationToken);
}
