# Cove UI Extensions

A multi-extension repository containing UI-focused extensions for [Cove](https://github.com/yourcove/cove).

## Extensions

The repo-level extension catalog is `extensions/catalog.json`. It is the local
source of truth for extension IDs, paths, tag prefixes, and whether an extension
has a UI bundle.

| Extension | ID | Description |
|-----------|-----|-------------|
| Audios | `cove.official.audios` | Full audio file management UI |
| Custom Home Page | `cove.official.custom-home-page` | Enhanced dashboard replacing the default home page |
| Scene Analytics | `cove.official.scene-analytics` | Play count tracking and analytics tab for scenes |

## Building

### Prerequisites

- .NET 10 SDK
- Node.js 24.x
- `Cove.Plugins` package from NuGet.org

Use the repo's `.nvmrc` to match the CI/runtime version exactly.

### Build All Extensions

```bash
dotnet build -c Release
```

### Package for Distribution

```bash
# Custom Home Page
dotnet publish extensions/CustomHomePage -c Release -o artifacts/custom-home-page
cd artifacts/custom-home-page && zip -r ../../cove.official.custom-home-page-1.0.0.zip . && cd ../..

# Scene Analytics  
dotnet publish extensions/SceneAnalytics -c Release -o artifacts/scene-analytics
cd artifacts/scene-analytics && zip -r ../../cove.official.scene-analytics-1.0.0.zip . && cd ../..

# Audios
dotnet publish extensions/Audios -c Release -o artifacts/audios
cd artifacts/audios && zip -r ../../cove.official.audios-1.0.0.zip . && cd ../..
```

### Run Extension Tests

```bash
dotnet test
```

### Validate Multi-Extension Metadata

```bash
npm run validate:extensions
```

This checks that `extensions/catalog.json` matches each `extension.json`, that
categories are lowercase kebab-case, and that each packaged manifest has an
extension-level `minCoveVersion` for direct URL installs. In the registry,
`minCoveVersion` moves to each `versions[]` entry.

Registry PR snippets should omit `checksum` and `releasedAt`; the registry CI
computes `checksum` during PR validation and stamps missing `releasedAt` when
merged to `main`.

## Development

Each extension is a standalone .NET class library that references `Cove.Plugins`. Extensions implement `IExtension` plus capability interfaces:

- `IUIExtension` — contribute pages, tabs, themes, settings panels
- `IApiExtension` — register custom API endpoints
- `IStatefulExtension` — persistent key-value storage
- `IJobExtension` — background jobs
- `IEventExtension` — entity lifecycle events

## Contracts Dependency Model

These projects are package-first and consume `Cove.Plugins` via `PackageReference`.

- CI pins `CovePluginsVersion` and restores from NuGet.org.
- Local contributors can opt into source-based development with:
	- `-p:UseLocalCovePlugins=true`
	when working in a monorepo checkout that includes `src/Cove.Plugins`.

See the [Cove Extension Development Guide](todo) for full documentation.

