using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Launcher.Core;

/// <summary>A Python that runs: where, which version, and whether `python` on PATH is it.</summary>
public sealed record PythonFound(string Executable, Version Version, bool OnPath)
{
    public string Folder => Path.GetDirectoryName(Executable) ?? "";
}

/// <summary>What looking for a Python found.</summary>
/// <param name="Usable">The newest Python 3.9 or newer; null when there is none.</param>
/// <param name="OnPath">The version `python` on PATH answers with; null when that is no Python: there is none, or the Microsoft
/// Store's shortcut answered.</param>
/// <param name="StoreAlias">`python` on PATH is the Microsoft Store's shortcut, which offers to install Python instead of running
/// one.</param>
public sealed record PythonSearch(PythonFound? Usable, Version? OnPath, bool StoreAlias);

/// <summary>Finds the Python the checkout's scripts need, 3.9 or newer. python.org's installer leaves "Add python.exe to PATH"
/// unticked, and Windows then answers `python` with the Microsoft Store's shortcut, so an installed Python is often not the one
/// PATH gives. On Windows the look goes on through the py launcher, the installers' registry entries (PEP 514) and python.org's
/// default folders, and what it finds goes first on this process's PATH (<see cref="Adopt"/>): `python` is then that one for
/// Waypoint and for everything it starts, the checkout's scripts included, as the installer's option would have made it.</summary>
public static class PythonLookup
{
    const int MinimumMajor = 3, MinimumMinor = 9;

    /// <summary>The version in `python --version`'s answer ("Python 3.12.4"); null when the answer is no version, such as the
    /// Store shortcut's advertisement.</summary>
    public static Version? VersionOf(string answer) =>
        Regex.Match(answer, @"\APython (\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant) is { Success: true } match
            ? new(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0) : null;

    public static bool NewEnough(Version version) => version.Major > MinimumMajor || version.Major == MinimumMajor && version.Minor >= MinimumMinor;

    public static async Task<PythonSearch> FindAsync()
    {
        var answer = await ToolProcess.AskAsync(Platform.Python, "--version");
        Version? onPath = answer is { Code: 0 } ok ? VersionOf(ok.Output) : null;
        // The shortcut exits with 9009 and advertises the Store.
        bool store = onPath is null && answer is { } reply && (reply.Code == 9009 || reply.Output.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase));
        if (onPath is not null && NewEnough(onPath))
        {
            string executable = Platform.OnPath(OperatingSystem.IsWindows() ? Platform.Python + ".exe" : Platform.Python) ?? Platform.Python;
            return new(new(executable, onPath, OnPath: true), onPath, false);
        }
        PythonFound? best = null;
        if (OperatingSystem.IsWindows())
            foreach (string candidate in await InstalledOnWindowsAsync())
            {
                var asked = await ToolProcess.AskAsync(candidate, "--version");
                if (asked is { Code: 0 } runs && VersionOf(runs.Output) is { } version && NewEnough(version) && (best is null || version > best.Version))
                    best = new(candidate, version, OnPath: false);
            }
        return new(best, onPath, store);
    }

    /// <summary>Every python.exe Windows knows of, on PATH or not: the py launcher's choice (python.org's installer puts the
    /// launcher in C:\Windows), the installers' registry entries, and python.org's default folders.</summary>
    [SupportedOSPlatform("windows")]
    static async Task<List<string>> InstalledOnWindowsAsync()
    {
        var found = new List<string>();
        void Add(string? executable)
        {
            try
            {
                if (executable is { Length: > 0 } && File.Exists(executable) && !found.Contains(Path.GetFullPath(executable), StringComparer.OrdinalIgnoreCase))
                    found.Add(Path.GetFullPath(executable));
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        }
        var launcher = await ToolProcess.AskAsync("py", "-3", "-c", "import sys; print(sys.executable)");
        if (launcher is { Code: 0 } chosen)
            Add(chosen.Output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)));
        foreach (var (hive, view) in new[] { (RegistryHive.CurrentUser, RegistryView.Default), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var python = root.OpenSubKey(@"Software\Python");
                if (python is null) continue;
                foreach (string company in python.GetSubKeyNames())
                {
                    using var tags = python.OpenSubKey(company);
                    if (tags is null) continue;
                    foreach (string tag in tags.GetSubKeyNames())
                    {
                        using var install = tags.OpenSubKey(tag + @"\InstallPath");
                        Add(install?.GetValue("ExecutablePath") as string ?? (install?.GetValue(null) is string folder ? Path.Combine(folder, "python.exe") : null));
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        }
        foreach (string parent in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\" })
        {
            try { foreach (string folder in Directory.EnumerateDirectories(parent, "Python3*")) Add(Path.Combine(folder, "python.exe")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return found;
    }

    /// <summary>The process's PATH with a Python's folder, and its Scripts where pip goes, first: what the installer's option
    /// would have put there.</summary>
    public static string AdoptedPath(string current, string folder)
    {
        string[] ahead = [folder, Path.Combine(folder, "Scripts")];
        var rest = current.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(entry => !ahead.Contains(entry.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase));
        return string.Join(';', ahead.Concat(rest));
    }

    /// <summary>Makes a Python found off PATH the `python` of this process and of everything it starts. Nothing outside the
    /// process changes: the player's own PATH stays theirs.</summary>
    public static void Adopt(PythonSearch search)
    {
        if (!OperatingSystem.IsWindows() || search.Usable is not { OnPath: false } found) return;
        Environment.SetEnvironmentVariable("PATH", AdoptedPath(Environment.GetEnvironmentVariable("PATH") ?? "", found.Folder));
    }

    /// <summary>The requirement's line: which Python counts and where, or why none does.</summary>
    public static string Describe(PythonSearch search) => search switch
    {
        { Usable: { OnPath: true, Folder.Length: > 0 } python } => L.Text("Python {0} is on PATH ({1}).", python.Version, python.Folder),
        { Usable: { OnPath: true } python } => L.Text("Python {0} is on PATH.", python.Version),
        { Usable: { } python } => L.Text("Python {0} is installed in {1} but not on PATH. Waypoint uses it anyway, for itself and the scripts it runs.", python.Version, python.Folder),
        { OnPath: { } old } when OperatingSystem.IsWindows() => L.Text("Python {0} is on PATH; 3.9 or newer is needed. Install it from python.org and tick 'Add python.exe to PATH'.", old),
        { OnPath: { } old } => L.Text("Python {0} is on PATH; 3.9 or newer is needed.", old),
        { StoreAlias: true } => L.Text("The Microsoft Store's python shortcut answered, not a Python. Install Python 3.9 or newer from python.org and tick 'Add python.exe to PATH'."),
        _ when OperatingSystem.IsWindows() => L.Text("Python is not on PATH. Install Python 3.9 or newer from python.org and tick 'Add python.exe to PATH'."),
        _ => L.Text("Python is not on PATH. Install Python 3.9 or newer (python3) with your package manager."),
    };
}
