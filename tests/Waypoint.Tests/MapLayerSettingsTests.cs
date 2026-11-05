using System.Text.Json;

namespace Waypoint.Tests;

public class MapLayerSettingsTests
{
    [Fact]
    public void LayersRoundTripAndOlderSettingsKeepExistingDefaults()
    {
        var settings = new Waypoint.Settings { MapLayers = new(false, true, false, true), Language = "zh-CN" };
        var restored = JsonSerializer.Deserialize<Waypoint.Settings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.MapLayers, restored.MapLayers);
        Assert.Equal("zh-CN", restored.Language);
        Assert.Equal(new Waypoint.MapLayerSettings(true, true, true, false), JsonSerializer.Deserialize<Waypoint.Settings>("{}")!.MapLayers);
    }
}
