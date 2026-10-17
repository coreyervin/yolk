namespace Yolk.TestSupport;

/// <summary>
/// Finds files elsewhere in the repo — the Mac goldens and version source —
/// so the Windows build is checked against them rather than against copies.
/// </summary>
public static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string Read(params string[] relativePath) =>
        // Normalised so a Windows checkout with CRLF line endings compares
        // equal to output written with "\n".
        File.ReadAllText(Path.Combine([Root, .. relativePath])).ReplaceLineEndings("\n");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Goldens"))
                && Directory.Exists(Path.Combine(dir.FullName, "windows")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("could not find the repo root above " + AppContext.BaseDirectory);
    }
}
