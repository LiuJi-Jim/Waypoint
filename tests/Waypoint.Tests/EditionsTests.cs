using Waypoint;

namespace Waypoint.Tests;

public sealed class EditionsTests
{
    [Fact]
    public void A_remembered_folder_counts_only_while_it_is_still_that_kind()
    {
        string folder = Path.Combine(Path.GetTempPath(), "waypoint-editions-" + Guid.NewGuid().ToString("N"));
        string release = Path.Combine(folder, "release"), latest = Path.Combine(folder, "latest");
        try
        {
            Directory.CreateDirectory(release);
            File.WriteAllText(Path.Combine(release, "LocalExile.exe"), "");
            File.WriteAllText(Path.Combine(release, "release.json"), """{"version":"0.1.0","commit":"cc0f9c3","client":"3.29.3b"}""");
            Directory.CreateDirectory(Path.Combine(latest, "scripts", "dev"));
            File.WriteAllText(Path.Combine(latest, "GameServer.slnx"), "");
            File.WriteAllText(Path.Combine(latest, "scripts", "dev", "live.ps1"), "");
            var settings = new Settings { ReleaseFolder = release, LatestFolder = latest };

            Assert.Equal(release, Editions.Folder(settings, release: true));
            Assert.Equal(latest, Editions.Folder(settings, release: false));

            // Swapped, or gone: neither side is offered as the other.
            Assert.Null(Editions.Folder(new Settings { ReleaseFolder = latest, LatestFolder = release }, release: true));
            Assert.Null(Editions.Folder(new Settings { ReleaseFolder = latest, LatestFolder = release }, release: false));
            Directory.Delete(release, recursive: true);
            Assert.Null(Editions.Folder(settings, release: true));
            Assert.Null(Editions.Folder(new Settings(), release: false));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
