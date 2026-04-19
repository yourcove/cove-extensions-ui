using Cove.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Extensions.CustomHomePage;

/// <summary>
/// Replaces the default home page with an enhanced dashboard featuring
/// recent activity, statistics, and quick actions.
/// </summary>
public class CustomHomePageExtension : IExtension, IUIExtension
{
    public string Id => "com.cove.custom-home-page";
    public string Name => "Custom Home Page";
    public string Version => "1.0.0";
    public string? Description => "Enhanced dashboard replacing the default home page with recent activity, statistics, and quick actions.";
    public string? Author => "Cove Team";
    public string? Url => "https://github.com/yourcove/cove-extensions-ui";
    public string? IconUrl => null;
    public IReadOnlyList<string> Categories => [ExtensionCategories.UI, ExtensionCategories.Layout];

    public void ConfigureServices(IServiceCollection services, ExtensionContext context) { }

    public UIManifest GetUIManifest() => new()
    {
        Pages =
        [
            new UIPage
            {
                Id = "custom-home",
                Path = "/",
                Label = "Dashboard",
                ComponentName = "CustomHomeDashboard",
                Icon = "home",
                NavGroup = null,
                ReplaceExisting = true,
            }
        ],
    };
}
