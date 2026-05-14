namespace Structura.Common;

public static class ProjectFolders
{
    public static string Root => AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf("bin", StringComparison.InvariantCulture)];
    public static string Samples => Path.Combine(Root, "Samples");
}
