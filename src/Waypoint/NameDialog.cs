using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>Asks for one name, as Mod Organizer does after Create, Copy or Rename; null when cancelled.</summary>
sealed class NameDialog : FormWindow
{
    readonly TextBox _name = new() { MaxLength = 64, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>The name typed, trimmed; null when the dialog was cancelled.</summary>
    public string? Chosen { get; private set; }

    public NameDialog(Window? owner, string title, string prompt, string initial = "")
    {
        DialogLayout.Configure(this, owner, title, 480);
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        _name.Text = initial;
        root.Children.Add(_name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = L.Text("OK"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = L.Text("Cancel"), IsCancel = true };
        ok.Click += (_, _) =>
        {
            Chosen = _name.Text.Trim();
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    /// <summary>The name, or null when cancelled.</summary>
    public static string? Ask(Window? owner, string title, string prompt, string initial = "")
    {
        var dialog = new NameDialog(owner, title, prompt, initial);
        return dialog.ShowDialog() == true && dialog.Chosen is { Length: > 0 } name ? name : null;
    }
}
