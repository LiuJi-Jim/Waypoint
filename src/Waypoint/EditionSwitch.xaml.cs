using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>Simple mode's Release | Latest switch, at the top of both Play views (<see cref="Editions"/>).</summary>
public partial class EditionSwitch : UserControl
{
    Shared _shared = null!;

    /// <summary>The player has no release yet and wants the setup guide's release route.</summary>
    internal event Action? ReleaseSetupRequested;
    /// <summary>The player has no Latest copy yet and wants the guide's source route.</summary>
    internal event Action? LatestSetupRequested;

    public EditionSwitch() => InitializeComponent();

    internal void Attach(Shared shared)
    {
        _shared = shared;
        L.Current.PropertyChanged += (_, _) => Show();
        Show();
    }

    void Show()
    {
        ReleaseChoice.IsChecked = Repo.IsInstall;
        LatestChoice.IsChecked = !Repo.IsInstall;
        Caption.Text = Repo.IsInstall ? L.Text("The tested release. Its characters are its own.")
            // The crafter's route can leave the copy on the tested version, which Play does not move.
            : SwitchToSource.OnTestedVersion(Environment.CurrentDirectory) ? L.Text("A tested version of LocalExile's source, kept as it is. Its characters are its own.")
            : L.Text("LocalExile's newest changes, updated when you play. Its characters are its own.");
    }

    void Chosen(object sender, RoutedEventArgs e) => Choose(sender == ReleaseChoice);

    /// <summary>The player chose a side, here or through the crafter's "Get the item crafter…".</summary>
    internal async void Choose(bool release)
    {
        var owner = Window.GetWindow(this);
        var (step, folder) = await Editions.PrepareAsync(_shared, release,
            (kind, title, message, buttons) => Dialog.Show(owner, kind, title, message, null, buttons));
        Show();
        if (step == EditionStep.SetUpRelease) ReleaseSetupRequested?.Invoke();
        else if (step == EditionStep.SetUpLatest) LatestSetupRequested?.Invoke();
        else if (step == EditionStep.Switch) App.SwitchServer(owner, _shared.Settings, folder);
    }
}
