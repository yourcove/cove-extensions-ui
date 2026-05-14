# Official Downloaders

Standalone Cove extension package that ships separate downloader providers:

- `cove.official.downloaders.ytdlp`: yt-dlp-powered video/audio downloads and scene metadata scraping.
- `cove.official.downloaders.reddit`: Reddit-hosted post image/video downloads plus diverted links to other registered downloaders.
- `cove.official.downloaders.common-audio`: direct hosted-audio downloads for Soundgasm and Whyp.
- `cove.official.downloaders.common-text`: common text downloads, currently Literotica.

The providers are intentionally separate. yt-dlp only handles downloads that run through yt-dlp. Reddit owns Reddit post media and returns diverted matches for external links so Cove can re-match those links against any registered downloader. Common audio/text providers own their direct site logic.

## Runtime dependency: yt-dlp

Only the yt-dlp provider needs `yt-dlp`. It resolves the executable like this:

1. If `COVE_YTDLP_PATH` is set, it uses that binary.
2. Else if `YT_DLP_PATH` is set, it uses that binary.
3. Else if the extension setting `YtDlpPath` is set in Cove, it uses that binary.
4. Else if a managed copy already exists in the extension directory under `tools/`, it uses that binary.
5. Else if `yt-dlp` is already on `PATH`, it uses it.
6. Else, on Windows and Linux, it downloads a managed copy into its extension directory under `tools/` the first time it is used.
7. On unsupported platforms for managed download, install `yt-dlp` yourself and point the extension at it with `COVE_YTDLP_PATH`, `YT_DLP_PATH`, or the extension setting.

The package manifest declares yt-dlp as a generic external dependency. Cove surfaces that declaration and the settings fields in the Extensions settings tab, but the yt-dlp provider owns all yt-dlp-specific resolution and command-line behavior.

## Auth, cookies, and impersonation

The yt-dlp provider supports the following extension settings and matching environment variables:

| Setting | Environment variable | yt-dlp argument |
| --- | --- | --- |
| `Impersonate` | `COVE_YTDLP_IMPERSONATE` or `YT_DLP_IMPERSONATE` | `--impersonate` |
| `CookiesPath` | `COVE_YTDLP_COOKIES` or `YT_DLP_COOKIES` | `--cookies` |
| `CookiesFromBrowser` | `COVE_YTDLP_COOKIES_FROM_BROWSER` or `YT_DLP_COOKIES_FROM_BROWSER` | `--cookies-from-browser` |
| `Proxy` | `COVE_YTDLP_PROXY` or `YT_DLP_PROXY` | `--proxy` |
| `Username` | `COVE_YTDLP_USERNAME` or `YT_DLP_USERNAME` | `--username` |
| `Password` | `COVE_YTDLP_PASSWORD` or `YT_DLP_PASSWORD` | `--password` |
| `UseNetrc` | `COVE_YTDLP_NETRC` or `YT_DLP_NETRC` | `--netrc` |

Environment variables take precedence over Cove extension settings. Password values saved in Cove are stored in the Cove config file, so prefer cookies or environment variables for sensitive shared deployments.

Sites that require browser impersonation need a yt-dlp build with impersonation support. The official standalone yt-dlp binaries normally cover the easiest native path; Python installs should include the curl-cffi extra, for example `python -m pip install "yt-dlp[default,curl-cffi]"`.

## Docker guidance

For deterministic containers, bake `yt-dlp` into your image and set `YT_DLP_PATH` or `COVE_YTDLP_PATH`.

```dockerfile
RUN python3 -m pip install --no-cache-dir "yt-dlp[default,curl-cffi]"
ENV YT_DLP_PATH=/usr/local/bin/yt-dlp
```

The managed download path is also supported when the container has outbound internet access and the Cove extensions directory is writable and persisted.

## Build

```powershell
dotnet build extensions/OfficialDownloaders/OfficialDownloaders.csproj
```

## Test

```powershell
dotnet test tests/OfficialDownloaders.Tests/OfficialDownloaders.Tests.csproj
```

## Package for registry release

```powershell
.\scripts\package-official-downloaders.ps1 -Version 1.0.0
```
