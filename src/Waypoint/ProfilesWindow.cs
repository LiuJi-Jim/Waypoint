using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>
/// LocalExile's game settings profiles, as Mod Organizer's Profiles window: the player's profiles on the left (the default and
/// those made here, the active one marked), the other folders under Documents\My Games\Path of Exile below them, and Select,
/// Create, Copy, Rename, Remove beside them (<see cref="PathChoices"/>). Create, Copy and Rename ask for the name. The other folders
/// can be selected and copied, not renamed or removed.
/// </summary>
sealed class ProfilesWindow : FormWindow
{
    readonly Shared _shared;
    readonly ListBox _list = new() { MinHeight = 220, Margin = new Thickness(0, 0, 12, 0) };
    readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    readonly Button _select, _create, _copy, _rename, _remove, _open;

    public ProfilesWindow(Window? owner, Shared shared)
    {
        _shared = shared;
        DialogLayout.Configure(this, owner, L.Text("Game settings profiles"), 720);
        var root = new DockPanel { Margin = new Thickness(24) };

        var close = new Button { Content = L.Text("Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            Text = L.Text("A profile is a folder of LocalExile's game settings: graphics, keys, network mode and saved login, in {0}. The game uses the active one from its next start.", ClientSettings.Documents) };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        _detail.SetResourceReference(StyleProperty, "Shell.Secondary");
        DockPanel.SetDock(_detail, Dock.Bottom);
        root.Children.Add(_detail);

        _select = Action(L.Text("Select"), L.Text("Use this profile from the game's next start."), Select);
        _create = Action(L.Text("Create…"), L.Text("A new profile from your normal game settings, without their saved login."), Create);
        _copy = Action(L.Text("Copy…"), L.Text("A new profile from the selected one, without its saved login."), Copy);
        _rename = Action(L.Text("Rename…"), L.Text("Only a profile made here, and not the active one."), Rename);
        _remove = Action(L.Text("Remove"), L.Text("Only a profile made here, never the active one or the last. It goes to the Recycle Bin."), Remove);
        _open = Action(L.Text("Open folder"), "", OpenFolder);
        var buttons = new StackPanel { Width = 150 };
        foreach (var button in new[] { _select, _create, _copy, _rename, _remove, _open }) buttons.Children.Add(button);
        DockPanel.SetDock(buttons, Dock.Right);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        _list.SelectionChanged += (_, _) => Enable();
        Content = root;
        Refresh(PathChoices.GameSettingsFolder());
    }

    Button Action(string text, string tip, Action run)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        if (tip.Length > 0) button.ToolTip = tip;
        button.Click += (_, _) => run();
        return button;
    }

    int Ask(DialogKind kind, string title, string message, string[] buttons) => Dialog.Show(this, kind, title, message, null, buttons);

    PathChoices.GameProfile? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as PathChoices.GameProfile;

    void Refresh(string? select)
    {
        _list.Items.Clear();
        var profiles = PathChoices.GameProfiles(_shared.Settings);
        // The player's own first; the rest of the folders there, quieter, under their heading.
        var others = profiles.Where(p => !PathChoices.IsYours(p)).ToList();
        foreach (var profile in profiles.Where(PathChoices.IsYours)) Add(profile, profile.Name + (profile.Active ? "  " + L.Text("(active)") : ""), select);
        if (others.Count > 0)
        {
            _list.Items.Add(new ListBoxItem { Content = L.Text("Other folders"), IsEnabled = false, Focusable = false, Margin = new Thickness(0, 8, 0, 0),
                FontWeight = FontWeights.SemiBold });
            foreach (var profile in others) Add(profile, profile.Name, select).SetResourceReference(ForegroundProperty, "Shell.TextSecondary");
        }
        _detail.Text = PathChoices.GameSettingsDetail();
        Enable();
    }

    ListBoxItem Add(PathChoices.GameProfile profile, string text, string? select)
    {
        var item = new ListBoxItem { Content = text, Tag = profile, FontWeight = profile.Active ? FontWeights.SemiBold : FontWeights.Normal };
        _list.Items.Add(item);
        if (string.Equals(profile.Name, select, StringComparison.OrdinalIgnoreCase)) _list.SelectedItem = item;
        return item;
    }

    void Enable()
    {
        bool client = Directory.Exists("Client");
        var selected = Selected;
        _select.IsEnabled = client && selected is { Active: false };
        _create.IsEnabled = client;
        _copy.IsEnabled = client && selected is not null;
        _rename.IsEnabled = selected is not null && PathChoices.CanRename(_shared.Settings, selected.Name);
        _remove.IsEnabled = selected is not null && PathChoices.CanRemove(_shared.Settings, selected.Name);
        _open.IsEnabled = selected is not null;
    }

    void Select()
    {
        if (Selected is { } profile && PathChoices.UseGameSettings(_shared, profile.Name, Ask)) Refresh(profile.Name);
    }

    void Create()
    {
        if (NameDialog.Ask(this, L.Text("Create a profile"), L.Text("Name of the new profile, from your normal game settings:")) is { } name
            && PathChoices.CreateGameSettings(_shared, name, null, Ask)) Refresh(name);
    }

    void Copy()
    {
        if (Selected is { } profile && NameDialog.Ask(this, L.Text("Copy a profile"), L.Text("Name of the copy of {0}:", profile.Name), profile.Name + " 2") is { } name
            && PathChoices.CreateGameSettings(_shared, name, profile.Name, Ask)) Refresh(name);
    }

    void Rename()
    {
        if (Selected is { } profile && NameDialog.Ask(this, L.Text("Rename a profile"), L.Text("New name for {0}:", profile.Name), profile.Name) is { } name
            && name != profile.Name && PathChoices.RenameGameSettings(_shared, profile.Name, name, Ask)) Refresh(name);
    }

    void Remove()
    {
        if (Selected is { } profile && PathChoices.RemoveGameSettings(_shared, profile.Name, Ask)) Refresh(PathChoices.GameSettingsFolder());
    }

    void OpenFolder()
    {
        if (Selected is { } profile && Path.Combine(ClientSettings.Documents, profile.Name) is var folder && Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false })?.Dispose();
    }
}
