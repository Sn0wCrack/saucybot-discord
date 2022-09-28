namespace SaucyBot.Library.Sites.ArtStation;

public interface IArtStationClient
{
    public Task<Project?> GetProject(string hash);
}
