using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>The game settings profile on Play, as Mod Organizer's profile list: pick one and the game uses it from its next start,
/// or Manage… for the Profiles window (<see cref="ProfilesWindow"/>). Shown only once there is a game client.</summary>
sealed class ProfilePicker : StackPanel
{
    const string Manage = "\u0001manage";
    Shared? _shared;
    readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    readonly ComboBox _list = new() { MinWidth = 220 };
    bool _loading;

    public ProfilePicker()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        Margin = new Thickness(0, 0, 0, 20);
        Visibility = Visibility.Collapsed;
        _label.SetResourceReference(StyleProperty, "Shell.Secondary");
        Children.Add(_label);
        Children.Add(_list);
        _list.SelectionChanged += Chosen;
        // Back on Play after Settings or another window: what the client uses may have changed.
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    internal void Attach(Shared shared)
    {
        _shared = shared;
        L.Current.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    void Refresh()
    {
        if (_shared is null) return;
        Visibility = Directory.Exists("Client") ? Visibility.Visible : Visibility.Collapsed;
        _loading = true;
        _label.Text = L.Text("Game settings profile:");
        _list.Items.Clear();
        var profiles = PathChoices.GameProfiles(_shared.Settings);
        // No profile yet: the normal settings are in use, until one is chosen.
        if (!profiles.Any(p => p.Active)) _list.Items.Add(new ComboBoxItem { Content = L.Text("Your normal settings"), Tag = "", IsSelected = true });
        foreach (var profile in profiles) _list.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Name, IsSelected = profile.Active });
        _list.Items.Add(new Separator());
        _list.Items.Add(new ComboBoxItem { Content = L.Text("Manage…"), Tag = Manage });
        _loading = false;
    }

    void Chosen(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _shared is not { } shared || _list.SelectedItem is not ComboBoxItem { Tag: string tag } || tag.Length == 0) return;
        var owner = Window.GetWindow(this);
        if (tag == Manage) new ProfilesWindow(owner, shared).ShowDialog();
        else if (!string.Equals(tag, PathChoices.GameSettingsFolder(), StringComparison.OrdinalIgnoreCase))
            PathChoices.UseGameSettings(shared, tag, (kind, title, message, buttons) => Dialog.Show(owner, kind, title, message, null, buttons));
        // After this selection's event: the list shows what is in use now, Manage… unselected.
        Dispatcher.BeginInvoke(Refresh);
    }
}
