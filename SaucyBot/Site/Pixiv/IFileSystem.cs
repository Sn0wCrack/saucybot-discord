namespace SaucyBot.Site.Pixiv;

public interface IFileSystem
{
    FileStream OpenRead(string path);
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);
    void DeleteDirectory(string path, bool recursive);
    bool DirectoryExists(string path);
}
