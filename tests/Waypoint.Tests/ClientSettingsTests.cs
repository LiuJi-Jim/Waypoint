using System.Text;

namespace Waypoint.Tests;

public class ClientSettingsTests : IDisposable
{
    readonly string _top = Path.Combine(Path.GetTempPath(), "Waypoint-settings-" + Guid.NewGuid().ToString("N"));
    string Client => Path.Combine(_top, "Client");
    string Documents => Path.Combine(_top, "My Games");
    string Shared => Path.Combine(Documents, "production_Config.ini");
    string Own => Path.Combine(Documents, "LocalExile", "production_Config.ini");
    string Config => Path.Combine(Client, "config.json");

    public ClientSettingsTests()
    {
        Directory.CreateDirectory(Client);
        Directory.CreateDirectory(Documents);
    }

    public void Dispose() => Directory.Delete(_top, recursive: true);

    ClientSettingsState Read(bool linked = false) => ClientSettings.Read(Client, linked, Documents);

    [Fact]
    public void A_client_of_its_own_without_a_config_file_shares_the_normal_settings()
    {
        Assert.Equal(new ClientSettingsState(ClientSettingsKind.Shared, Shared), Read());
        File.WriteAllText(Config, """{ "GGG_CONFIG_FILE_NAME": "PRODUCTION_config.ini" }""");
        Assert.Equal(ClientSettingsKind.Shared, Read().Kind);
        File.WriteAllText(Config, """{ "GGG_LOGIN_SERVER_PORT": "20481" }""");
        Assert.Equal(ClientSettingsKind.Shared, Read().Kind);
    }

    [Fact]
    public void A_named_file_is_separate_and_is_the_one_the_client_uses()
    {
        File.WriteAllText(Config, """{ "GGG_CONFIG_FILE_NAME": "evryPath\\production_Config.ini" }""");
        var state = Read();
        Assert.Equal(ClientSettingsKind.Separate, state.Kind);
        Assert.Equal(Path.Combine(Documents, "evryPath", "production_Config.ini"), state.ConfigFile);
        Assert.Equal(ClientSettingsKind.Separate, Read(linked: true).Kind);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    public void A_config_file_that_cannot_be_read_is_unknown(string text)
    {
        File.WriteAllText(Config, text);
        Assert.Equal(new ClientSettingsState(ClientSettingsKind.Unknown, Shared), Read());
    }

    [Fact]
    public void A_link_without_its_own_file_is_unknown_because_something_else_may_start_from_it() =>
        Assert.Equal(ClientSettingsKind.Unknown, Read(linked: true).Kind);

    [Fact]
    public void Own_settings_start_from_the_normal_ones_without_the_login_and_leave_those_alone()
    {
        string[] normal = ["[DISPLAY]", "resolution_width=2560", "", "[LOGIN]", "account_name=someone", "username=someone@example.com", "", "[NETWORKING]", "networking_type=auto", "", "[UI]", "scale=1"];
        File.WriteAllLines(Shared, normal, new UTF8Encoding(true));
        byte[] before = File.ReadAllBytes(Shared);

        ClientSettings.UseOwn(Client, Documents);

        Assert.Equal(before, File.ReadAllBytes(Shared));
        Assert.Equal(new ClientSettingsState(ClientSettingsKind.Separate, Own), Read());
        string own = File.ReadAllText(Own);
        Assert.Contains("resolution_width=2560", own);
        Assert.Contains("scale=1", own);
        Assert.DoesNotContain("someone", own);
        Assert.DoesNotContain("LOGIN", own);
        Assert.True(ClientSettings.IsPredictive(Own));
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(Own)[..3]);
        Assert.Contains(@"LocalExile\\production_login.credentials", File.ReadAllText(Config));
    }

    [Fact]
    public void A_pc_that_never_ran_the_game_gets_a_settings_file_with_the_network_mode()
    {
        ClientSettings.UseOwn(Client, Documents);
        Assert.False(File.Exists(Shared));
        Assert.True(ClientSettings.IsPredictive(Own));
    }

    [Fact]
    public void Existing_own_settings_are_kept_and_only_the_network_mode_changes()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Own)!);
        File.WriteAllLines(Own, ["[DISPLAY]", "resolution_width=800", "[NETWORKING]", "networking_type=lockstep", "other=1"]);
        File.WriteAllLines(Shared, ["[DISPLAY]", "resolution_width=2560"]);

        ClientSettings.UseOwn(Client, Documents);

        Assert.Equal(["[DISPLAY]", "resolution_width=800", "[NETWORKING]", "networking_type=predictive", "other=1"], File.ReadAllLines(Own));
    }

    [Fact]
    public void Other_keys_in_a_config_file_stay()
    {
        File.WriteAllText(Config, """{ "GGG_LOGIN_SERVER_PORT": "20481" }""");
        ClientSettings.UseOwn(Client, Documents);
        Assert.Contains("GGG_LOGIN_SERVER_PORT", File.ReadAllText(Config));
        Assert.Equal(ClientSettingsKind.Separate, Read().Kind);
    }

    [Fact]
    public void A_config_file_that_cannot_be_read_is_set_aside_not_lost()
    {
        File.WriteAllText(Config, "{ not json");
        ClientSettings.UseOwn(Client, Documents);
        Assert.Equal("{ not json", File.ReadAllText(Config + ".old"));
        Assert.Equal(ClientSettingsKind.Separate, Read().Kind);
    }

    [Fact]
    public void A_stop_part_way_leaves_nothing_half_written()
    {
        ClientSettings.UseOwn(Client, Documents);
        Assert.Empty(Directory.EnumerateFiles(_top, "*.new", SearchOption.AllDirectories));
    }

    [Fact]
    public void Play_offers_once_to_a_whole_client_without_settings_of_its_own()
    {
        Assert.False(ClientSettings.ShouldOffer(Client, offered: false, Documents));
        File.WriteAllText(Path.Combine(Client, "Content.ggpk"), "stand-in");
        Assert.True(ClientSettings.ShouldOffer(Client, offered: false, Documents));
        Assert.False(ClientSettings.ShouldOffer(Client, offered: true, Documents));
        ClientSettings.UseOwn(Client, Documents);
        Assert.False(ClientSettings.ShouldOffer(Client, offered: false, Documents));
    }

    [Fact]
    public void No_network_mode_is_no_answer()
    {
        Assert.Null(ClientSettings.NetworkMode(Shared));
        File.WriteAllLines(Shared, ["[UI]", "scale=1"]);
        Assert.False(ClientSettings.IsPredictive(Shared));
        File.WriteAllLines(Shared, ["[NETWORKING]", "NETWORKING_TYPE=Predictive"]);
        Assert.True(ClientSettings.IsPredictive(Shared));
    }

    [Fact]
    public void Game_settings_folders_are_listed_made_from_the_one_in_use_without_its_login_and_switched_to()
    {
        File.WriteAllLines(Shared, ["[DISPLAY]", "fullscreen=true", "[LOGIN]", "login_email=me@example.invalid"]);
        File.WriteAllText(Config, """{ "GGG_LOGIN_SERVER_PORT": "20481" }""");
        ClientSettings.UseOwn(Client, Documents);
        File.AppendAllLines(Own, ["[NETWORKING]", "networking_type=lockstep", "[LOGIN]", "login_email=own@example.invalid"]);
        File.WriteAllText(Path.Combine(Documents, "LocalExile", "production_login.credentials"), "saved");
        Directory.CreateDirectory(Path.Combine(Documents, "Screenshots"));
        Assert.Equal("LocalExile", ClientSettings.Profile(Client, Documents));

        // A copy of the folder in use, as it is but for the saved login; listed with it, not the folders without settings.
        ClientSettings.CreateProfile("Test", "LocalExile", Documents);
        string copy = File.ReadAllText(Path.Combine(Documents, "Test", "production_Config.ini"));
        Assert.Contains("networking_type=lockstep", copy);
        Assert.DoesNotContain("login_email", copy);
        Assert.False(File.Exists(Path.Combine(Documents, "Test", "production_login.credentials")));
        Assert.Equal(["LocalExile", "Test"], ClientSettings.Profiles(Documents));

        // Switching names it in config.json, which keeps what else it holds; the normal settings are only read.
        ClientSettings.UseProfile(Client, "Test", Documents);
        Assert.Equal("Test", ClientSettings.Profile(Client, Documents));
        Assert.Contains("20481", File.ReadAllText(Config));
        Assert.Contains("login_email=me@example.invalid", File.ReadAllText(Shared));

        Assert.Throws<IOException>(() => ClientSettings.CreateProfile("Test", null, Documents));
        Assert.Throws<IOException>(() => ClientSettings.UseProfile(Client, "Screenshots", Documents));
        Assert.All(["", "..", "a\\b", "a/b", " x", "x:"], name => Assert.False(ClientSettings.ValidProfile(name)));
    }
}
