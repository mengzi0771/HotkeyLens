namespace HotkeyLens.Core.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        var root = System.IO.Path.Combine(AppContext.BaseDirectory, ".test-temp");
        Directory.CreateDirectory(root);
        Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}