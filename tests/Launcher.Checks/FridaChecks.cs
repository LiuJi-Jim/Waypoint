using Launcher.Core;

static class FridaChecks
{
    public static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        string root = Path.Combine(fixture, "optional frida");
        Directory.CreateDirectory(root);
        int code = await ToolProcess.RunAsync(Platform.Python, ["-m", "venv", "--without-pip", Path.Combine(root, ".venv")],
            root, Console.WriteLine, CancellationToken.None);
        check(code == 0, "Frida checks use a disposable Python environment without downloading packages");
        string python = Platform.VenvPython(root);
        var answer = await ToolProcess.AskAsync(python, "-I", "-c", "import sysconfig; print(sysconfig.get_path('purelib'))");
        string site = answer is { Code: 0 } result ? result.Output.Trim() : throw new IOException("No fixture site-packages");
        string package = Path.Combine(site, "frida");
        Directory.CreateDirectory(package);
        string module = Path.Combine(package, "__init__.py");
        File.WriteAllText(module, "raise ImportError('fixture native library cannot load')\n");
        check(SwitchToSource.HasFrida(root) && !await SwitchToSource.FridaAvailableAsync(root),
            "a Frida package directory whose import fails is not treated as ready");
        File.WriteAllText(module, "def attach(*args): pass\n");
        check(await SwitchToSource.FridaAvailableAsync(root), "the checkout's own working Frida is used even when it is not on PATH");

        // A private fake pip makes installation deterministic: it never contacts a package server.
        Directory.Delete(package, recursive: true);
        string pip = Path.Combine(site, "pip");
        Directory.CreateDirectory(pip);
        File.WriteAllText(Path.Combine(pip, "__init__.py"), "");
        File.WriteAllText(Path.Combine(pip, "__main__.py"), """
            import pathlib, sys
            home = pathlib.Path(__file__).parent
            assert sys.prefix != sys.base_prefix
            assert sys.argv[1:] == ['install', '--disable-pip-version-check', 'frida']
            content = "raise ImportError('fixture broken install')\n" if (home / 'broken').exists() else 'def attach(*args): pass\n'
            (home.parent / 'frida.py').write_text(content)
            """);
        await SwitchToSource.InstallFridaAsync(root, Console.WriteLine, CancellationToken.None);
        check(await SwitchToSource.FridaAvailableAsync(root), "installation targets the existing private environment and verifies Frida before returning");
        File.WriteAllText(Path.Combine(pip, "broken"), "");
        bool refused = false;
        try { await SwitchToSource.InstallFridaAsync(root, _ => { }, CancellationToken.None); }
        catch (IOException) { refused = true; }
        check(refused, "a successful pip exit with an unloadable Frida is reported as installation failure");
    }
}
