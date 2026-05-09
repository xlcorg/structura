namespace Structura.Common;

public static class FileExtensions
{
    public static string AppendPath(
        this string path1,
        params string[] paths)
    {
        if (paths.Any(Path.IsPathRooted))
        {
            throw new ArgumentException("Cannot append path to a rooted path");
        }

        return Path.Combine([path1, ..paths]);
    }

    public static string ReadAllText(
        this string file)
    {
        return File.ReadAllText(file);
    }

    public static string WriteAllText(
        this string file,
        string content)
    {
        File.WriteAllText(file, content);

        return file;
    }
}
