using Launcher.Core;

namespace Waypoint;

/// <summary>Which of the chosen paths a row of Settings → Folders and tools is.</summary>
enum PathKind { Data, Backups, WinePrefix, Tool }

/// <summary>
/// Settings → Folders and tools: Waypoint's data folder, the backups folder, the Linux Wine prefix and the program for each of
/// LocalExile's tools. An empty choice is the default, and every choice can be taken back. Moving data never leaves the player
/// stuck: a move that cannot be done changes nothing and says how to do it by hand, and a folder can always be used as it is.
/// </summary>
static class PathChoices
{
    /// <summary>A dialog: its buttons' index, -1 when closed (Dialog.Show on either front end).</summary>
    public delegate int Ask(DialogKind kind, string title, string message, string[] buttons);

    /// <summary>The folder or program in use now for a row.</summary>
    public static string Now(PathKind kind, string? tool = null) => kind switch
    {
        PathKind.Data => SettingsStore.DataDirectory,
        PathKind.Backups => SettingsStore.BackupsDirectory,
        PathKind.WinePrefix => Platform.GamePrefix,
        _ => ToolPaths.Usable(tool!) ?? "",
    };

    /// <summary>A new data or backups folder (null: the default). True when it was saved and Waypoint must start again.</summary>
    public static async Task<bool> ChangeFolderAsync(Shared shared, PathKind kind, string? chosen, Ask ask, Action<string> open)
    {
        var settings = shared.Settings;
        string from = Now(kind);
        string to = chosen is { Length: > 0 } ? Path.GetFullPath(chosen.Trim())
            : kind == PathKind.Data ? SettingsStore.DefaultDataDirectory : Path.Combine(SettingsStore.DataDirectory, "backups");
        string what = kind == PathKind.Data ? L.Text("Waypoint's data") : L.Text("the save backups");
        if (string.Equals(Path.TrimEndingDirectorySeparator(from), Path.TrimEndingDirectorySeparator(to), StringComparison.OrdinalIgnoreCase))
        {
            Save(settings, kind, chosen);
            return false;
        }
        if (shared.IsLiveRunning || shared.Monitor.Latest.Any(s => s.Running))
        {
            ask(DialogKind.Info, L.Text("Stop LocalExile first"), L.Text("Waypoint moves its files only while nothing runs. Press Stop playing, then try again."), [L.Text("OK")]);
            return false;
        }
        // A backups folder of its own inside the data folder would move along and be lost to its setting.
        if (kind == PathKind.Data && settings.BackupsFolder is { Length: > 0 } backups
            && Path.GetFullPath(backups).StartsWith(Path.TrimEndingDirectorySeparator(from) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            ask(DialogKind.Warning, L.Text("Your backups folder is inside it"),
                L.Text("Choose another backups folder first, or the default one, then move Waypoint's data."), [L.Text("OK")]);
            return false;
        }
        if (Folders.Unusable(to) is { } why)
        {
            ask(DialogKind.Warning, L.Text("That folder can't be used"), L.Text("Waypoint cannot write to {0}: {1}", to, why), [L.Text("OK")]);
            return false;
        }
        bool move;
        if (Folders.Empty(to))
        {
            int answer = ask(DialogKind.Question, L.Text("Move {0} to {1}?", what, to),
                L.Text("Waypoint copies the files there, checks them, removes the old ones and starts again. If anything goes wrong, nothing changes."),
                [L.Text("Move and start again"), L.Text("Use the folder as it is"), L.Text("Cancel")]);
            if (answer is < 0 or 2) return false;
            move = answer == 0;
        }
        else
        {
            // Only into an empty folder; one with files in it (moved there by hand, say) is used as it is.
            int answer = ask(DialogKind.Question, L.Text("{0} is not empty", to),
                L.Text("Waypoint moves files only into an empty folder. You can use this one as it is, with what is in it; the files in {0} stay there.", from),
                [L.Text("Use it as it is"), L.Text("Cancel")]);
            if (answer != 0) return false;
            move = false;
        }
        if (move)
        {
            IReadOnlyList<string> left;
            try { left = await Task.Run(() => Folders.Move(from, to)); }
            catch (IOException e)
            {
                if (ask(DialogKind.Error, L.Text("The files weren't moved"),
                        e.Message + "\n\n" + L.Text("Waypoint still uses {0}. You can move the files yourself, then choose the new folder again and pick Use it as it is.", from),
                        [L.Text("Open both folders"), L.Text("OK")]) == 0)
                {
                    open(from);
                    if (Directory.Exists(to)) open(to);
                }
                return false;
            }
            if (left.Count > 0)
                ask(DialogKind.Info, L.Text("Some old files stayed behind"),
                    L.Text("Everything is in {0}, but these could not be removed from {1}; you can delete them:", to, from) + "\n" + string.Join("\n", left.Take(10)),
                    [L.Text("OK")]);
        }
        Save(settings, kind, chosen);
        return true;
    }

    static void Save(Settings settings, PathKind kind, string? chosen)
    {
        string? value = chosen is { Length: > 0 } ? Path.GetFullPath(chosen.Trim()) : null;
        if (kind == PathKind.Data) settings.DataFolder = value; else settings.BackupsFolder = value;
        settings.Save();
    }

    /// <summary>The Linux Wine prefix (null: WINEPREFIX, else Waypoint's own). Nothing is moved: the game sets a new prefix up
    /// when it starts in it, and the old one stays where it is.</summary>
    public static void ChangeWinePrefix(Settings settings, string? chosen, Ask ask)
    {
        settings.WinePrefix = chosen is { Length: > 0 } ? Path.GetFullPath(chosen.Trim()) : null;
        settings.Save();
        Platform.ChosenGamePrefix = settings.WinePrefix;
        ask(DialogKind.Info, L.Text("The game uses it from its next start"), L.Text("The old prefix stays where it is: {0}", Platform.GamePrefix), [L.Text("OK")]);
    }

    /// <summary>The program for a tool (null: found as before). One that does not answer when asked its version is used only
    /// when the player says so. True when it was saved.</summary>
    public static async Task<bool> ChangeToolAsync(Settings settings, string tool, string? chosen, Ask ask)
    {
        string? program = chosen is { Length: > 0 } ? chosen.Trim().Trim('"') : null;
        if (program is not null && !File.Exists(program))
        {
            ask(DialogKind.Warning, L.Text("That program isn't there"), program, [L.Text("OK")]);
            return false;
        }
        var before = new Dictionary<string, string>(settings.Tools, StringComparer.OrdinalIgnoreCase);
        if (program is null) settings.Tools.Remove(tool); else settings.Tools[tool] = program;
        ToolPaths.Choose(settings.Tools);
        if (program is not null && await ToolPaths.VersionAsync(tool) is null
            && ask(DialogKind.Warning, L.Text("{0} didn't answer", program), L.Text("Asked its version, it did not answer. Use it anyway?"),
                [L.Text("Use it anyway"), L.Text("Cancel")]) != 0)
        {
            settings.Tools = before;
            ToolPaths.Choose(before);
            return false;
        }
        settings.Save();
        return true;
    }

    /// <summary>What a row says under its field: the version and program in use, or why there is none.</summary>
    public static async Task<string> DescribeToolAsync(Settings settings, string tool)
    {
        string? chosen = settings.Tools.GetValueOrDefault(tool);
        var found = await ToolPaths.VersionAsync(tool);
        string now = found is { } f ? L.Text("In use: {0} ({1})", f.Version, f.Program) : L.Text("Not found");
        return chosen is { Length: > 0 } && ToolPaths.Usable(tool) is null ? L.Text("{0} is not there, so Waypoint finds it itself.", chosen) + " " + now : now;
    }
}
