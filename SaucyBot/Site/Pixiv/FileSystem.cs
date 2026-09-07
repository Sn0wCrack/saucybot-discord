namespace SaucyBot.Site.Pixiv;

public sealed class FileSystem : IFileSystem
{
    public FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, cancellationToken);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public bool DirectoryExists(string path) => Directory.Exists(path);
}
