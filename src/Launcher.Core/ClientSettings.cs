using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Core;

/// <summary>Shared: the files every Path of Exile on this PC uses. Separate: files of their own. Unknown: Waypoint cannot tell
/// what a change would reach, so the player decides.</summary>
public enum ClientSettingsKind { Shared, Separate, Unknown }

/// <param name="ConfigFile">The settings file the client uses.</param>
public sealed record ClientSettingsState(ClientSettingsKind Kind, string ConfigFile);

/// <summary>
/// Where a client keeps its settings and saved login. Retail, Steam and Epic all use production_Config.ini and
/// production_login.credentials in Documents\My Games\Path of Exile, and so does a LocalExile client, unless a config.json in
/// the folder it starts from names other files, relative to that folder. The shared files are only ever read.
/// </summary>
public static class ClientSettings
{
    const string ConfigKey = "GGG_CONFIG_FILE_NAME", CredentialKey = "GGG_CREDENTIAL_FILE_NAME";
    const string SharedConfig = "production_Config.ini", OwnFolder = "LocalExile";
    const string OwnConfig = OwnFolder + @"\" + SharedConfig, OwnCredential = OwnFolder + @"\production_login.credentials";
    const string NetworkKey = "networking_type=", NetworkSection = "[NETWORKING]";

    public static string Documents => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Path of Exile");

    /// <summary>The folder a separate client's settings go in, for the player to read.</summary>
    public static string OwnFolderPath => Path.Combine(Documents, OwnFolder);

    public static bool IsLink(string client)
    {
        try { return new DirectoryInfo(client).LinkTarget is not null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static ClientSettingsState Read(string client, string? documents = null) => Read(client, IsLink(client), documents);

    /// <summary>A folder of the client's own that names no file uses the shared ones. A link is someone else's folder as well, so
    /// a change there reaches whatever else starts from it.</summary>
    /// <param name="linked">The client is, or will be, a link to this folder rather than a copy of it.</param>
    public static ClientSettingsState Read(string client, bool linked, string? documents = null)
    {
        documents ??= Documents;
        string shared = Path.Combine(documents, SharedConfig), file = Path.Combine(client, "config.json");
        var named = new ClientSettingsState(linked ? ClientSettingsKind.Unknown : ClientSettingsKind.Shared, shared);
        if (!File.Exists(file)) return named;
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(file));
            if (config.RootElement.ValueKind != JsonValueKind.Object) return new(ClientSettingsKind.Unknown, shared);
            if (!config.RootElement.TryGetProperty(ConfigKey, out var name) || name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 } own
                || own.Equals(SharedConfig, StringComparison.OrdinalIgnoreCase)) return named;
            return new(ClientSettingsKind.Separate, Path.GetFullPath(Path.Combine(documents, own)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new(ClientSettingsKind.Unknown, shared);
        }
    }

    /// <summary>Why Waypoint cannot tell, for a player deciding.</summary>
    public static string WhyUnknown(string client, bool linked) => linked
        ? L.Text("This client is a link to {0}, and anything else started from that folder would use the same settings.", Path.TrimEndingDirectorySeparator(ClientLocator.RealPath(client)))
        : L.Text("Its config.json could not be read. It is kept beside the new one as config.json.old.");

    /// <summary>The network mode in a settings file; null when it names none or cannot be read.</summary>
    public static string? NetworkMode(string configFile)
    {
        try
        {
            return File.ReadLines(configFile).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(NetworkKey, StringComparison.OrdinalIgnoreCase))?[NetworkKey.Length..];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static bool IsPredictive(string configFile) => string.Equals(NetworkMode(configFile), "predictive", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gives the client LocalExile's own files. A new settings file starts as a copy of the shared one without its saved
    /// login, and the client needs Predictive whichever it is. config.json goes last, so a stop leaves the client as it was.</summary>
    public static void UseOwn(string client, string? documents = null)
    {
        documents ??= Documents;
        Directory.CreateDirectory(Path.Combine(documents, OwnFolder));
        Seed(Path.Combine(documents, OwnConfig), Path.Combine(documents, SharedConfig));
        Point(Path.Combine(client, "config.json"));
    }

    static void Seed(string own, string shared)
    {
        bool exists = File.Exists(own);
        string[] lines = exists ? File.ReadAllLines(own) : File.Exists(shared) ? [.. WithoutLogin(File.ReadAllLines(shared))] : [];
        string[] wanted = Predictive(lines);
        if (exists && lines.SequenceEqual(wanted)) return;
        // The byte-order mark is how the client writes this file.
        Replace(own, string.Join(Environment.NewLine, wanted) + Environment.NewLine, new UTF8Encoding(true));
    }

    static IEnumerable<string> WithoutLogin(IEnumerable<string> lines)
    {
        bool login = false;
        foreach (string line in lines)
        {
            string text = line.Trim();
            if (text.StartsWith('[') && text.EndsWith(']')) login = text.Equals("[LOGIN]", StringComparison.OrdinalIgnoreCase);
            if (!login) yield return line;
        }
    }

    static string[] Predictive(string[] lines)
    {
        const string Line = NetworkKey + "predictive";
        var result = lines.ToList();
        int section = result.FindIndex(l => l.Trim().Equals(NetworkSection, StringComparison.OrdinalIgnoreCase));
        if (section < 0)
        {
            if (result.Count > 0 && result[^1].Trim().Length > 0) result.Add("");
            result.AddRange([NetworkSection, Line]);
            return [.. result];
        }
        int end = result.FindIndex(section + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = result.Count;
        int found = result.FindIndex(section + 1, end - section - 1, l => l.TrimStart().StartsWith(NetworkKey, StringComparison.OrdinalIgnoreCase));
        if (found >= 0) result[found] = Line;
        else result.Insert(section + 1, Line);
        return [.. result];
    }

    /// <summary>Keeps what else the file holds. One that cannot be added to stays beside the new one.</summary>
    static void Point(string file)
    {
        JsonObject config = [];
        if (File.Exists(file))
        {
            try { config = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new JsonException(); }
            catch (JsonException)
            {
                File.Move(file, Unused(file + ".old"));
                config = [];
            }
        }
        config[ConfigKey] = OwnConfig;
        config[CredentialKey] = OwnCredential;
        Replace(file, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
    }

    static string Unused(string path)
    {
        string name = path;
        for (int i = 2; File.Exists(name); i++) name = $"{path}{i}";
        return name;
    }

    static void Replace(string file, string text, Encoding encoding)
    {
        string staged = file + ".new";
        File.WriteAllText(staged, text, encoding);
        File.Move(staged, file, overwrite: true);
    }
}
