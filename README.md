# Cove UI Extensions

A multi-extension repository containing UI-focused extensions for [Cove](https://github.com/yourcove/cove).

## Extensions

| Extension | ID | Description |
|-----------|-----|-------------|
| Custom Home Page | `com.cove.custom-home-page` | Enhanced dashboard replacing the default home page |
| Scene Analytics | `com.cove.scene-analytics` | Play count tracking and analytics tab for scenes |

## Building

### Prerequisites

- .NET 10 SDK
- Cove.Plugins NuGet package (or local reference)

### Build All Extensions

```bash
dotnet build -c Release
```

### Package for Distribution

```bash
# Custom Home Page
dotnet publish extensions/CustomHomePage -c Release -o artifacts/custom-home-page
cd artifacts/custom-home-page && zip -r ../../com.cove.custom-home-page-1.0.0.zip . && cd ../..

# Scene Analytics  
dotnet publish extensions/SceneAnalytics -c Release -o artifacts/scene-analytics
cd artifacts/scene-analytics && zip -r ../../com.cove.scene-analytics-1.0.0.zip . && cd ../..
```

## Development

Each extension is a standalone .NET class library that references `Cove.Plugins`. Extensions implement `IExtension` plus capability interfaces:

- `IUIExtension` — contribute pages, tabs, themes, settings panels
- `IApiExtension` — register custom API endpoints
- `IStatefulExtension` — persistent key-value storage
- `IJobExtension` — background jobs
- `IEventExtension` — entity lifecycle events

See the [Cove Extension Development Guide](todo) for full documentation.

