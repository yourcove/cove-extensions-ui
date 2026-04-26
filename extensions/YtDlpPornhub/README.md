# Pornhub Downloader (yt-dlp)

Standalone Cove extension that provides:

- a Pornhub scene downloader
- a Pornhub scene metadata scraper
- automatic quality selection via yt-dlp format filtering
- inline scene metadata application after download

## Runtime dependency: yt-dlp

This extension is intentionally no longer part of Cove core. It is shipped independently and manages `yt-dlp` like this:

1. If `COVE_YTDLP_PATH` is set, the extension uses that binary.
2. Else if `YT_DLP_PATH` is set, the extension uses that binary.
3. Else if `yt-dlp` is already on `PATH`, the extension uses it.
4. Else, on Windows and Linux, the extension downloads a managed copy into its own extension directory under `tools/` the first time it is used.
5. On unsupported platforms for managed download, install `yt-dlp` yourself and point the extension at it with `COVE_YTDLP_PATH` or `YT_DLP_PATH`.

That means the extension stays independent of Cove core while still being usable out of the box on normal desktop installs.

## Docker guidance

For Docker there are three supported models.

### Recommended: bake yt-dlp into the image

Install `yt-dlp` in the image and set `YT_DLP_PATH`.

```dockerfile
RUN python3 -m pip install --no-cache-dir yt-dlp
ENV YT_DLP_PATH=/usr/local/bin/yt-dlp
```

This is the most deterministic option for production containers.

### Supported: let the extension auto-download yt-dlp

If the container has outbound internet access and the extensions directory is writable, the extension can download its own managed copy on first use.

For this to persist across container restarts, persist the Cove config/extensions volume.

### Air-gapped/offline containers

Mount a host-provided `yt-dlp` binary into the container and point `YT_DLP_PATH` (or `COVE_YTDLP_PATH`) at it.

## Build

```powershell
dotnet build extensions/YtDlpPornhub/YtDlpPornhub.csproj
```

## Test

```powershell
dotnet test tests/YtDlpPornhub.Tests/YtDlpPornhub.Tests.csproj
```

## Package for registry release

```powershell
pwsh ./scripts/package-ytdlp-pornhub.ps1 -Version 1.0.0
```

The packaging script publishes the extension, zips it as:

- `cove.official.ytdlp.pornhub-<version>.zip`

and prints the SHA-256 checksum plus a registry `metadata.json` snippet you can paste into the official extension registry.
