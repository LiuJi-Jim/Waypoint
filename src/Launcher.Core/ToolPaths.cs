namespace Launcher.Core;

/// <summary>
/// The programs the player chose for LocalExile's tools (Settings → Folders and tools): Git, the .NET SDK, Node.js, Python and
/// PowerShell. A chosen program's folder goes first on Waypoint's own PATH, which everything it starts inherits, live.ps1 and
/// LocalExile's scripts included. A choice whose program is gone is left out, and the tool is then found as it always was.
/// </summary>
public static class ToolPaths
{
    /// <summary>The tools, by the name each answers to on PATH.</summary>
    public static readonly string[] Names = ["git", "dotnet", "node", "python", "powershell"];

    static Dictionary<string, string> _chosen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The player's choices (empty values ignored), put first on PATH at once.</summary>
    public static void Choose(IReadOnlyDictionary<string, string>? chosen)
    {
        _chosen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, path) in chosen ?? new Dictionary<string, string>())
            if (Names.Contains(name, StringComparer.OrdinalIgnoreCase) && path.Trim() is { Length: > 0 } program) _chosen[name] = program;
        Apply();
    }

    /// <summary>The program chosen for a tool; null when none is.</summary>
    public static string? Chosen(string name) => _chosen.GetValueOrDefault(name);

    /// <summary>The program chosen for a tool, while it exists; null otherwise.</summary>
    public static string? Usable(string name) => Chosen(name) is { } program && File.Exists(program) ? program : null;

    /// <summary>The chosen programs' folders first on this process's PATH. Called again after anything rebuilds PATH.</summary>
    public static void Apply()
    {
        var folders = Names.Select(Usable).OfType<string>().Select(Path.GetDirectoryName).OfType<string>().ToArray();
        if (folders.Length == 0) return;
        string current = Environment.GetEnvironmentVariable("PATH") ?? "";
        string front = Front(current, folders);
        if (front != current) Environment.SetEnvironmentVariable("PATH", front);
    }

    /// <summary>PATH with these folders first, each once, and the rest in its order.</summary>
    public static string Front(string path, IEnumerable<string> folders)
    {
        var first = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rest = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !first.Contains(p.Trim('"'), StringComparer.OrdinalIgnoreCase));
        return string.Join(Path.PathSeparator, first.Concat(rest));
    }

    /// <summary>The tool's version line and the program that answered: the chosen one, else the one PATH finds. Null when none
    /// runs. It only reports: an older version is shown, never refused.</summary>
    public static async Task<(string Version, string Program)?> VersionAsync(string name)
    {
        string program = Usable(name) ?? (name == "powershell" ? Platform.PowerShell : name);
        string[] arguments = name == "powershell" ? ["-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"] : ["--version"];
        if (await ToolProcess.AskAsync(program, arguments) is not { Code: 0 } answer) return null;
        string version = answer.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        string where = File.Exists(program) ? program : Platform.OnPath(OperatingSystem.IsWindows() ? program + ".exe" : program) ?? program;
        return (version, where);
    }
}
