using System.Text.Json;
using Cove.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Extensions.SceneAnalytics;

/// <summary>
/// Adds play count tracking and an analytics tab to scene detail pages.
/// </summary>
public class SceneAnalyticsExtension : IExtension, IApiExtension, IUIExtension, IStatefulExtension
{
    public string Id => "cove.official.scene-analytics";
    public string Name => "Scene Analytics";
    public string Version => "1.0.0";
    public string? Description => "Play count tracking and analytics tab for scenes with viewing history and statistics.";
    public string? Author => "Cove Team";
    public string? Url => "https://github.com/yourcove/cove-extensions-ui";
    public string? IconUrl => null;
    public IReadOnlyList<string> Categories => [ExtensionCategories.UI, ExtensionCategories.Analytics];

    private IExtensionStore? _store;

    public void ConfigureServices(IServiceCollection services, ExtensionContext context) { }

    public void SetStore(IExtensionStore store) => _store = store;

    public UIManifest GetUIManifest() => new()
    {
        Tabs =
        [
            new UITabContribution(
                Key: "scene-analytics-tab",
                Label: "Analytics",
                PageType: "scene",
                ExtensionId: Id,
                ComponentName: "SceneAnalyticsTab",
                Order: 100
            )
        ],
    };

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ext/scene-analytics");

        group.MapGet("/stats/{sceneId:int}", async (int sceneId) =>
        {
            if (_store == null) return Results.StatusCode(503);

            var playCountStr = await _store.GetAsync($"play_count:{sceneId}");
            var lastPlayedStr = await _store.GetAsync($"last_played:{sceneId}");

            return Results.Ok(new
            {
                sceneId,
                playCount = int.TryParse(playCountStr, out var pc) ? pc : 0,
                lastPlayed = lastPlayedStr,
            });
        });

        group.MapPost("/track/{sceneId:int}", async (int sceneId) =>
        {
            if (_store == null) return Results.StatusCode(503);

            var countStr = await _store.GetAsync($"play_count:{sceneId}") ?? "0";
            var newCount = int.TryParse(countStr, out var c) ? c + 1 : 1;

            await _store.SetAsync($"play_count:{sceneId}", newCount.ToString());
            await _store.SetAsync($"last_played:{sceneId}", DateTime.UtcNow.ToString("o"));

            // Append to history (store last 50 entries)
            var historyStr = await _store.GetAsync($"history:{sceneId}") ?? "[]";
            var history = JsonSerializer.Deserialize<List<string>>(historyStr) ?? [];
            history.Add(DateTime.UtcNow.ToString("o"));
            if (history.Count > 50) history = history.Skip(history.Count - 50).ToList();
            await _store.SetAsync($"history:{sceneId}", JsonSerializer.Serialize(history));

            return Results.Ok(new { playCount = newCount });
        });
    }
}
