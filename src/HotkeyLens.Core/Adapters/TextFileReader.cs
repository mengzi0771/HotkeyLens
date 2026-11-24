using System.Text;

namespace HotkeyLens.Core.Adapters;

internal static class TextFileReader
{
    private const long MaximumBytes = 8 * 1024 * 1024;

    public static bool TryReadAllText(string path, out string text, out string? error)
    {
        text = string.Empty;
        error = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = "文件不存在。";
                return false;
            }

            if (info.Length > MaximumBytes)
            {
                error = "文件超过 8 MB，为避免占用过多内存已跳过。";
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
            return true;
        }
        catch (IOException exception)
        {
            error = exception.Message;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}