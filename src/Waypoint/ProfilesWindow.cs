using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>
/// LocalExile's game settings profiles, as Mod Organizer's Profiles window: the profiles on the left, the active one marked, and
/// Select, Create, Copy, Rename, Remove beside them (<see cref="PathChoices"/>). Every folder under Documents\My Games\Path of
/// Exile with settings is listed; those Waypoint did not make can be selected and copied, not renamed or removed.
/// </summary>
sealed class ProfilesWindow : FormWindow
{
    readonly Shared _shared;
    readonly ListBox _list = new() { MinHeight = 220, Margin = new Thickness(0, 0, 12, 0) };
    readonly TextBox _name = new() { MaxLength = 64 };
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
        var nameRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var nameLabel = new TextBlock { Text = L.Text("Name for Create, Copy or Rename:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(nameLabel, Dock.Left);
        nameRow.Children.Add(nameLabel);
        nameRow.Children.Add(_name);
        var bottom = new StackPanel();
        bottom.Children.Add(nameRow);
        bottom.Children.Add(_detail);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        _select = Action(L.Text("Select"), L.Text("Use this profile from the game's next start."), Select);
        _create = Action(L.Text("Create"), L.Text("A new profile from your normal game settings, without their saved login."), () => Make(null));
        _copy = Action(L.Text("Copy"), L.Text("A new profile from the selected one, without its saved login."), () => Make(Selected?.Name));
        _rename = Action(L.Text("Rename"), L.Text("Only a profile made here, and not the active one."), Rename);
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
        foreach (var profile in PathChoices.GameProfiles(_shared.Settings))
        {
            // LocalExile is Waypoint's own default; the rest it did not make may be another copy's or another tool's.
            string mark = profile.Made ? "" : string.Equals(profile.Name, ClientSettings.DefaultProfile, StringComparison.OrdinalIgnoreCase)
                ? "  " + L.Text("· default") : "  " + L.Text("· not made here");
            string text = profile.Name + (profile.Active ? "  " + L.Text("(active)") : "") + mark;
            var item = new ListBoxItem { Content = text, Tag = profile, FontWeight = profile.Active ? FontWeights.SemiBold : FontWeights.Normal };
            _list.Items.Add(item);
            if (string.Equals(profile.Name, select, StringComparison.OrdinalIgnoreCase)) _list.SelectedItem = item;
        }
        _detail.Text = PathChoices.GameSettingsDetail();
        Enable();
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

    void Make(string? from)
    {
        string name = _name.Text.Trim();
        if (PathChoices.CreateGameSettings(_shared, name, from, Ask))
        {
            _name.Text = "";
            Refresh(name);
        }
    }

    void Rename()
    {
        string name = _name.Text.Trim();
        if (Selected is { } profile && PathChoices.RenameGameSettings(_shared, profile.Name, name, Ask))
        {
            _name.Text = "";
            Refresh(name);
        }
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
