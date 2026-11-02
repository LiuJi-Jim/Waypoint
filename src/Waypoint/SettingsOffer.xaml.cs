using System.Windows;
using System.Windows.Controls;

namespace Waypoint;

/// <summary>Play's one offer of game settings of its own (<see cref="ClientSettings"/>) to a setup that has none. A new setup has
/// them from the guide, and Advanced mode's Setup section can do this at any time. Either answer ends the offer.</summary>
public partial class SettingsOffer : UserControl
{
    Settings _settings = null!;

    public SettingsOffer() => InitializeComponent();

    internal void Attach(Settings settings) => _settings = settings;

    /// <param name="idle">Nothing is running, and the setup is finished.</param>
    internal void Show(bool idle)
    {
        bool offer = idle && ClientSettings.ShouldOffer("Client", _settings.GameSettingsOffered);
        Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
        if (offer) OfferText.Text = ClientSettings.Explain("Client");
    }

    void Answered()
    {
        _settings.GameSettingsOffered = true;
        _settings.Save();
        Visibility = Visibility.Collapsed;
    }

    void UseClicked(object sender, RoutedEventArgs e)
    {
        try { ClientSettings.UseOwn("Client"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialog.Tell(Window.GetWindow(this), DialogKind.Error, L.Text("Game settings were not changed"), ex.Message);
            return;
        }
        Answered();
    }

    void DeclineClicked(object sender, RoutedEventArgs e) => Answered();
}
