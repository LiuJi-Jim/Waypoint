using System.Windows;

namespace Waypoint;

static class FridaSetup
{
    public static async Task<bool> EnsureAsync(Shared shared, Window? owner, string feature, Action<string> output)
    {
        bool ready = false;
        bool ran = await shared.RunExclusiveAsync(L.Text("Check Frida"), async () =>
        {
            ready = await PrepareAsync(shared, owner, feature, output);
        });
        if (!ran) output(L.Text("A workspace operation is in progress."));
        return ready;
    }

    // The caller holds Shared's checkout lock, including while the player answers the question.
    public static async Task<bool> PrepareAsync(Shared shared, Window? owner, string feature, Action<string> output)
    {
        string root = Environment.CurrentDirectory;
        if (await SwitchToSource.FridaAvailableAsync(root)) return true;
        if (!Dialog.Ask(owner, DialogKind.Question, L.Text("Install Frida?"),
                L.Text("{0} needs Frida to talk to the running game. Frida is optional for normal play. Install it in this LocalExile folder? No global Python packages will be changed.", feature),
                L.Text("Install Frida"), L.Text("Not now"))) return false;
        output(L.Text("Installing Frida …"));
        var progress = new Progress<string>(line => { shared.Logs.Write($"Frida: {line}"); output(line); });
        try
        {
            await SwitchToSource.InstallFridaAsync(root, line => ((IProgress<string>)progress).Report(line), CancellationToken.None);
            output(L.Text("Frida is ready."));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            shared.Logs.Write($"Frida: {ex.Message}", Severity.Error);
            output(ex.Message);
            Dialog.Tell(owner, DialogKind.Error, L.Text("Frida setup failed"), ex.Message);
            return false;
        }
    }
}
