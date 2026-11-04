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

    // ---- LocalExile's game settings profiles (Windows), as Mod Organizer's profiles: a folder each under
    // Documents\My Games\Path of Exile, the active one named in Client\config.json. Every folder there is listed; only those
    // made here can be renamed or removed, never the active one, and there is always one left. ----

    const string Client = "Client";

    /// <summary>A profile as the Profiles window and the Play picker list it.</summary>
    public sealed record GameProfile(string Name, bool Active, bool Made);

    /// <summary>The active profile, by name (null when the client uses the normal settings, or has none yet).</summary>
    public static string? GameSettingsFolder() => Directory.Exists(Client) ? ClientSettings.Profile(Client) : null;

    /// <summary>Every profile there is, the active one and those made here marked.</summary>
    public static IReadOnlyList<GameProfile> GameProfiles(Settings settings)
    {
        string? active = GameSettingsFolder();
        var names = ClientSettings.Profiles().ToList();
        // The client names a folder that is not there (deleted by hand): it is still the one in use, and the game makes it anew.
        if (active is not null && !names.Contains(active, StringComparer.OrdinalIgnoreCase)) names.Add(active);
        return [.. names.Order(StringComparer.OrdinalIgnoreCase).Select(name => new GameProfile(name, string.Equals(name, active, StringComparison.OrdinalIgnoreCase),
            settings.MadeGameSettings.Contains(name, StringComparer.OrdinalIgnoreCase)))];
    }

    /// <summary>Rename is offered for a profile made here that is not the active one.</summary>
    public static bool CanRename(Settings settings, string name) =>
        settings.MadeGameSettings.Contains(name, StringComparer.OrdinalIgnoreCase) && !string.Equals(name, GameSettingsFolder(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Remove too, and never the last profile.</summary>
    public static bool CanRemove(Settings settings, string name) => CanRename(settings, name) && ClientSettings.Profiles().Count > 1;

    /// <summary>What the Settings row and the Profiles window say: the profile in use, and what a switch reaches.</summary>
    public static string GameSettingsDetail()
    {
        if (!Directory.Exists(Client)) return L.Text("No game client yet: the setup guide adds one.");
        var state = ClientSettings.Read(Client);
        string text = state.Kind == ClientSettingsKind.Separate
            ? L.Text("In use: {0}", Path.GetDirectoryName(state.ConfigFile) ?? state.ConfigFile)
            : L.Text("In use: your normal Path of Exile settings ({0}). Select or create a profile to give LocalExile its own.", ClientSettings.Documents);
        if (ClientSettings.IsLink(Client)) text += " " + L.Text("This game client is shared with other LocalExile copies: a switch changes theirs too.");
        return text;
    }

    static bool Closed(Shared shared, Ask ask)
    {
        if (!shared.Monitor.Latest.Any(s => s.Service == Service.Client && s.Running)) return true;
        ask(DialogKind.Info, L.Text("Close the game first"), L.Text("The game reads its settings when it starts."), [L.Text("OK")]);
        return false;
    }

    static bool Named(string name, Ask ask)
    {
        if (ClientSettings.ValidProfile(name)) return true;
        ask(DialogKind.Warning, L.Text("Choose another name"), L.Text("A plain folder name, without \\ / : * ? \" < > |."), [L.Text("OK")]);
        return false;
    }

    static void Made(Settings settings, string name)
    {
        if (settings.MadeGameSettings.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        settings.MadeGameSettings.Add(name);
        settings.Save();
    }

    /// <summary>Select: the client uses this profile from its next start; only config.json changes. True when it did.</summary>
    public static bool UseGameSettings(Shared shared, string name, Ask ask)
    {
        if (!Directory.Exists(Client) || !Closed(shared, ask)) return false;
        if (ClientSettings.IsLink(Client) && ask(DialogKind.Warning, L.Text("Switch for every copy?"),
                L.Text("This game client is shared with other LocalExile copies, so they will use {0} too.", name),
                [L.Text("Switch"), L.Text("Cancel")]) != 0) return false;
        try { ClientSettings.UseProfile(Client, name); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ask(DialogKind.Error, L.Text("The game settings weren't switched"),
                e.Message + "\n\n" + L.Text("Nothing was changed. The file that names them is {0}.", Path.GetFullPath(Path.Combine(Client, "config.json"))), [L.Text("OK")]);
            return false;
        }
    }

    /// <summary>Create (<paramref name="from"/> null: from the normal settings, no login, Predictive) or Copy (from that profile,
    /// as it is, without its saved login). The new profile is listed, not selected. True when it was made.</summary>
    public static bool CreateGameSettings(Shared shared, string name, string? from, Ask ask)
    {
        name = name.Trim();
        if (!Named(name, ask)) return false;
        try { ClientSettings.CreateProfile(name, from); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ask(DialogKind.Error, L.Text("The profile wasn't made"), e.Message, [L.Text("OK")]);
            return false;
        }
        Made(shared.Settings, name);
        return true;
    }

    /// <summary>Rename: a profile made here, not the active one. True when it was renamed.</summary>
    public static bool RenameGameSettings(Shared shared, string name, string newName, Ask ask)
    {
        newName = newName.Trim();
        if (!CanRename(shared.Settings, name) || !Named(newName, ask)) return false;
        try { ClientSettings.RenameProfile(name, newName, Client); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ask(DialogKind.Error, L.Text("The profile wasn't renamed"), e.Message, [L.Text("OK")]);
            return false;
        }
        shared.Settings.MadeGameSettings.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        Made(shared.Settings, newName);
        return true;
    }

    /// <summary>Remove: a profile made here, not the active one, never the last, to the Recycle Bin after the player says so; its
    /// saved login goes with it. True when it went.</summary>
    public static bool RemoveGameSettings(Shared shared, string name, Ask ask)
    {
        if (!CanRemove(shared.Settings, name)) return false;
        string folder = Path.Combine(ClientSettings.Documents, name);
        if (ask(DialogKind.Warning, L.Text("Remove {0}?", name),
                L.Text("Its game settings and saved login go to the Recycle Bin, where you can restore them. Another LocalExile copy that uses it would start without them.") + "\n\n" + folder,
                [L.Text("Remove"), L.Text("Cancel")]) != 0) return false;
        try
        {
            if (Directory.Exists(folder))
#if WINDOWS
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
#else
                Directory.Delete(folder, recursive: true);
#endif
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            ask(DialogKind.Error, L.Text("{0} wasn't removed", name), e.Message + "\n\n" + L.Text("You can delete the folder yourself: {0}", folder), [L.Text("OK")]);
            return false;
        }
        shared.Settings.MadeGameSettings.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        shared.Settings.Save();
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
