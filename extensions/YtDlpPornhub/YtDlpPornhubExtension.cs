using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Cove.Core.DTOs;
using Cove.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Extensions.YtDlpPornhub;

public sealed class YtDlpPornhubExtension : IDownloaderProvider, IScraperProvider
{
    private const string ExtensionId = "cove.official.ytdlp.pornhub";
    private const string DownloaderId = "builtin.ytdlp/pornhub-scene";
    private const string ScraperId = "builtin.ytdlp/pornhub-scene-metadata";
    private const string RepoUrl = "https://github.com/yourcove/cove-extensions-ui";
    private static readonly DownloaderDescriptor Downloader = new(
        DownloaderId,
        "Pornhub (yt-dlp)",
        DownloaderEntity.Scene,
        ["pornhub.com/view_video.php?viewkey=*", "*.pornhub.com/view_video.php?viewkey=*"],
        DownloaderCapabilities.MultiQuality | DownloaderCapabilities.ResumeSupported | DownloaderCapabilities.InlineMetadata);
    private static readonly ScraperDescriptor Scraper = new(
        ScraperId,
        "Pornhub (yt-dlp)",
        ScraperEntity.Scene,
        ScraperCapabilities.ByUrl | ScraperCapabilities.ByFragment,
        ["pornhub.com/view_video.php?viewkey=*", "*.pornhub.com/view_video.php?viewkey=*"],
        ScraperRiskLevel.NetworkOnly);

    private IYtDlpCommandRunner? _runner;
    private IServiceProvider? _services;
    private IConfiguration? _configuration;
    private string? _extensionRoot;

    public YtDlpPornhubExtension()
    {
    }

    public YtDlpPornhubExtension(IYtDlpCommandRunner runner)
    {
        _runner = runner;
    }

    public string Id => ExtensionId;
    public string Name => "Pornhub Downloader (yt-dlp)";
    public string Version => "1.0.0";
    public string? Description => "Standalone Pornhub scene downloader and metadata scraper powered by yt-dlp.";
    public string? Author => "Cove Team";
    public string? Url => RepoUrl;
    public string? IconUrl => null;
    public IReadOnlyList<string> Categories => [ExtensionCategories.Downloader, ExtensionCategories.Scraper, ExtensionCategories.Metadata];

    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        _configuration = context.Configuration;
        _extensionRoot = Path.Combine(context.DataDirectory, Id);
    }

    public Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        _services = services;
        Directory.CreateDirectory(GetExtensionRoot());
        _runner ??= CreateRunner();
        return Task.CompletedTask;
    }

    public IReadOnlyList<DownloaderDescriptor> GetDownloaders() => [Downloader];

    public IReadOnlyList<ScraperDescriptor> GetScrapers() => [Scraper];

    public async Task<DownloaderUrlMatch?> MatchAsync(string url, CancellationToken ct)
    {
        if (!IsSupportedUrl(url))
            return null;

        var info = await GetSceneInfoAsync(url, ct);
        return new DownloaderUrlMatch(
            Downloader.Id,
            info.NormalizedUrl,
            BuildQualityOptions(info.AvailableHeights),
            info.Title);
    }

    public async Task<ScrapedSceneDto?> ScrapeSceneAsync(ScraperRequest<SceneScrapeInput> request, CancellationToken ct)
    {
        if (!string.Equals(request.ScraperId, Scraper.Id, StringComparison.OrdinalIgnoreCase))
            return null;

        var targetUrl = ResolveSceneUrl(request.Input);
        if (string.IsNullOrWhiteSpace(targetUrl) || !IsSupportedUrl(targetUrl))
            return null;

        var info = await GetSceneInfoAsync(targetUrl, ct);
        return info.Metadata;
    }

    public async Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
    {
        if (!string.Equals(request.DownloaderId, Downloader.Id, StringComparison.OrdinalIgnoreCase))
            return null;

        if (request.Entity != DownloaderEntity.Scene)
            throw new InvalidOperationException("The Pornhub yt-dlp downloader only supports scene downloads.");

        if (!IsSupportedUrl(request.Url))
            throw new InvalidOperationException("This downloader only supports Pornhub scene URLs.");

        host.ReportProgress(0.05d, "Resolving Pornhub video metadata...");
        var info = await GetSceneInfoAsync(request.Url, ct);

        host.ReportProgress(0.15d, $"Downloading {info.Title}...");
        var outputTemplate = Path.Combine(host.TempDirectory, "downloaded.%(ext)s");
        var command = await GetRunner().RunAsync(
            [
                "--no-playlist",
                "--no-warnings",
                "--newline",
                "--no-part",
                "--output",
                outputTemplate,
                "--format",
                BuildFormatSelector(request.QualityId),
                info.NormalizedUrl,
            ],
            ct);

        EnsureSuccess(command, "yt-dlp failed to download the Pornhub video");

        var downloadedFile = FindDownloadedFile(host.TempDirectory);
        if (downloadedFile == null)
            throw new InvalidOperationException("yt-dlp completed successfully but did not leave a downloaded media file in the temp directory.");

        host.ReportProgress(0.95d, "Download completed.");

        var extension = Path.GetExtension(downloadedFile);
        var originalFilename = BuildOriginalFileName(info, extension);
        return new DownloaderResult(Path.GetFileName(downloadedFile), originalFilename, InlineSceneMetadata: info.Metadata);
    }

    public interface IYtDlpCommandRunner
    {
        Task<YtDlpCommandResult> RunAsync(IEnumerable<string> arguments, CancellationToken ct);
    }

    public sealed record YtDlpCommandResult(int ExitCode, string StandardOutput, string StandardError);

    private IYtDlpCommandRunner GetRunner() => _runner ??= CreateRunner();

    private IYtDlpCommandRunner CreateRunner()
    {
        if (_services == null)
            throw new InvalidOperationException("The yt-dlp extension has not been initialized yet.");

        var loggerFactory = _services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<YtDlpPornhubExtension>() ?? NullLogger<YtDlpPornhubExtension>.Instance;
        var resolver = new YtDlpExecutableResolver(
            Id,
            GetExtensionRoot(),
            _configuration,
            _services.GetRequiredService<IHttpClientFactory>(),
            logger);

        return new ProcessYtDlpCommandRunner(resolver);
    }

    private string GetExtensionRoot()
    {
        if (string.IsNullOrWhiteSpace(_extensionRoot))
            throw new InvalidOperationException("The yt-dlp extension root directory was not configured.");

        return _extensionRoot;
    }

    private static bool IsSupportedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        return host.Equals("pornhub.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".pornhub.com", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<YtDlpSceneInfo> GetSceneInfoAsync(string url, CancellationToken ct)
    {
        var command = await GetRunner().RunAsync(
            [
                "--skip-download",
                "--dump-single-json",
                "--no-playlist",
                "--no-warnings",
                url,
            ],
            ct);

        EnsureSuccess(command, "yt-dlp failed to extract Pornhub metadata");

        try
        {
            using var document = JsonDocument.Parse(command.StandardOutput);
            var root = document.RootElement;

            var normalizedUrl = root.TryGetProperty("webpage_url", out var webpageUrlElement)
                ? webpageUrlElement.GetString()
                : null;
            var title = root.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()
                : null;
            var videoId = root.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;

            return new YtDlpSceneInfo(
                string.IsNullOrWhiteSpace(normalizedUrl) ? url : normalizedUrl.Trim(),
                string.IsNullOrWhiteSpace(title) ? "Pornhub scene" : title.Trim(),
                string.IsNullOrWhiteSpace(videoId) ? null : videoId.Trim(),
                ExtractAvailableHeights(root),
                BuildSceneMetadata(
                    root,
                    string.IsNullOrWhiteSpace(normalizedUrl) ? url : normalizedUrl.Trim(),
                    string.IsNullOrWhiteSpace(title) ? "Pornhub scene" : title.Trim(),
                    string.IsNullOrWhiteSpace(videoId) ? null : videoId.Trim()));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("yt-dlp returned invalid JSON for the Pornhub URL.", ex);
        }
    }

    private static IReadOnlyList<DownloaderQualityOption> BuildQualityOptions(IReadOnlyList<int> heights)
    {
        var options = new List<DownloaderQualityOption>
        {
            new("best", "Best available", "Highest quality stream that yt-dlp can download")
        };

        foreach (var height in heights)
        {
            options.Add(new DownloaderQualityOption(
                $"max-height-{height}",
                $"{height}p",
                $"Best downloadable stream at or below {height}p"));
        }

        return options;
    }

    private static IReadOnlyList<int> ExtractAvailableHeights(JsonElement root)
    {
        var heights = new SortedSet<int>(Comparer<int>.Create((left, right) => right.CompareTo(left)));
        if (root.TryGetProperty("formats", out var formatsElement) && formatsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var format in formatsElement.EnumerateArray())
            {
                if (!format.TryGetProperty("height", out var heightElement) || !heightElement.TryGetInt32(out var height) || height <= 0)
                    continue;

                if (format.TryGetProperty("vcodec", out var videoCodecElement) && string.Equals(videoCodecElement.GetString(), "none", StringComparison.OrdinalIgnoreCase))
                    continue;

                heights.Add(height);
            }
        }

        if (heights.Count == 0 && root.TryGetProperty("height", out var fallbackHeightElement) && fallbackHeightElement.TryGetInt32(out var fallbackHeight) && fallbackHeight > 0)
            heights.Add(fallbackHeight);

        return heights.ToList();
    }

    private static string BuildFormatSelector(string? qualityId)
    {
        if (string.IsNullOrWhiteSpace(qualityId) || string.Equals(qualityId, "best", StringComparison.OrdinalIgnoreCase))
            return "best";

        const string prefix = "max-height-";
        if (qualityId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(qualityId[prefix.Length..], out var height)
            && height > 0)
        {
            return $"best[height<={height}]/best";
        }

        return "best";
    }

    private static ScrapedSceneDto BuildSceneMetadata(JsonElement root, string normalizedUrl, string title, string? videoId)
    {
        var urls = new List<string>();
        AddIfPresent(urls, normalizedUrl);
        AddIfPresent(urls, GetString(root, "original_url"));

        var performerNames = ExtractStringArray(root, "cast");
        var uploader = GetString(root, "uploader");
        AddIfPresent(performerNames, uploader);

        var tagNames = ExtractStringArray(root, "tags");
        if (tagNames.Count == 0)
            tagNames = ExtractStringArray(root, "categories");

        return new ScrapedSceneDto
        {
            Title = title,
            Code = videoId,
            Details = GetString(root, "description"),
            Date = FormatUploadDate(GetString(root, "upload_date"), root),
            ImageUrl = GetString(root, "thumbnail") ?? ExtractThumbnail(root),
            Urls = urls,
            StudioName = GetString(root, "channel", "channel_name"),
            PerformerNames = performerNames,
            TagNames = tagNames,
        };
    }

    private static string? ResolveSceneUrl(SceneScrapeInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Url) && IsSupportedUrl(input.Url))
            return input.Url.Trim();

        foreach (var candidate in input.Urls)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && IsSupportedUrl(candidate))
                return candidate.Trim();
        }

        if (string.IsNullOrWhiteSpace(input.Code))
            return null;

        var code = input.Code.Trim();
        if (IsSupportedUrl(code))
            return code;

        return $"https://www.pornhub.com/view_video.php?viewkey={Uri.EscapeDataString(code)}";
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }

    private static List<string> ExtractStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            AddIfPresent(values, item.GetString());
        }

        return values;
    }

    private static string? ExtractThumbnail(JsonElement root)
    {
        if (!root.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            var url = GetString(thumbnail, "url");
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private static string? FormatUploadDate(string? uploadDate, JsonElement root)
    {
        if (!string.IsNullOrWhiteSpace(uploadDate)
            && uploadDate.Length == 8
            && DateOnly.TryParseExact(uploadDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd");
        }

        if (root.TryGetProperty("timestamp", out var timestampElement) && timestampElement.TryGetInt64(out var timestamp))
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd");

        return null;
    }

    private static void AddIfPresent(List<string> values, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        var trimmed = candidate.Trim();
        if (values.Any(value => string.Equals(value, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;

        values.Add(trimmed);
    }

    private static string? FindDownloadedFile(string directory)
    {
        return Directory.EnumerateFiles(directory, "downloaded.*", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private static string BuildOriginalFileName(YtDlpSceneInfo info, string extension)
    {
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".mp4" : extension;
        return string.IsNullOrWhiteSpace(info.VideoId)
            ? $"{info.Title}{safeExtension}"
            : $"{info.Title} [{info.VideoId}]{safeExtension}";
    }

    private static void EnsureSuccess(YtDlpCommandResult command, string message)
    {
        if (command.ExitCode == 0)
            return;

        var detail = GetCommandDetail(command);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail}");
    }

    private static string GetCommandDetail(YtDlpCommandResult command)
    {
        var detail = string.IsNullOrWhiteSpace(command.StandardError)
            ? command.StandardOutput
            : command.StandardError;

        return detail
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
    }

    private static async Task<YtDlpCommandResult> RunProcessAsync(string executable, IEnumerable<string> arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"Unable to start yt-dlp using '{executable}'. Install yt-dlp, set COVE_YTDLP_PATH / YT_DLP_PATH, or allow the extension to download its managed copy.", ex);
        }

        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(ct);

        return new YtDlpCommandResult(
            process.ExitCode,
            (await stdoutTask).Trim(),
            (await stderrTask).Trim());
    }

    private sealed record YtDlpSceneInfo(string NormalizedUrl, string Title, string? VideoId, IReadOnlyList<int> AvailableHeights, ScrapedSceneDto Metadata);

    private sealed class ProcessYtDlpCommandRunner(YtDlpExecutableResolver executableResolver) : IYtDlpCommandRunner
    {
        public async Task<YtDlpCommandResult> RunAsync(IEnumerable<string> arguments, CancellationToken ct)
        {
            var executable = await executableResolver.ResolveAsync(ct);
            return await RunProcessAsync(executable, arguments, ct);
        }
    }

    private sealed class YtDlpExecutableResolver(
        string extensionId,
        string extensionRoot,
        IConfiguration? configuration,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        private readonly SemaphoreSlim _resolutionLock = new(1, 1);
        private string? _resolvedExecutable;

        public async Task<string> ResolveAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedExecutable) && await IsUsableAsync(_resolvedExecutable, ct))
                return _resolvedExecutable;

            await _resolutionLock.WaitAsync(ct);
            try
            {
                if (!string.IsNullOrWhiteSpace(_resolvedExecutable) && await IsUsableAsync(_resolvedExecutable, ct))
                    return _resolvedExecutable;

                var configuredPath = GetConfiguredExecutable();
                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    var normalizedConfiguredPath = Path.GetFullPath(configuredPath);
                    if (!await IsUsableAsync(normalizedConfiguredPath, ct))
                        throw new InvalidOperationException($"yt-dlp is configured at '{normalizedConfiguredPath}' but is not executable.");

                    _resolvedExecutable = normalizedConfiguredPath;
                    return normalizedConfiguredPath;
                }

                var managedPath = GetManagedBinaryPath();
                if (await IsUsableAsync(managedPath, ct))
                {
                    _resolvedExecutable = managedPath;
                    return managedPath;
                }

                if (await IsUsableAsync("yt-dlp", ct))
                {
                    _resolvedExecutable = "yt-dlp";
                    return "yt-dlp";
                }

                _resolvedExecutable = await DownloadManagedBinaryAsync(ct);
                return _resolvedExecutable;
            }
            finally
            {
                _resolutionLock.Release();
            }
        }

        private string? GetConfiguredExecutable()
        {
            var fromCoveEnv = Environment.GetEnvironmentVariable("COVE_YTDLP_PATH");
            if (!string.IsNullOrWhiteSpace(fromCoveEnv))
                return fromCoveEnv.Trim();

            var fromGenericEnv = Environment.GetEnvironmentVariable("YT_DLP_PATH");
            if (!string.IsNullOrWhiteSpace(fromGenericEnv))
                return fromGenericEnv.Trim();

            var fromConfig = configuration?[$"Extensions:{extensionId}:YtDlpPath"];
            return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig.Trim();
        }

        private string GetManagedBinaryPath()
        {
            var fileName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
            return Path.Combine(extensionRoot, "tools", fileName);
        }

        private async Task<bool> IsUsableAsync(string executable, CancellationToken ct)
        {
            try
            {
                if (Path.IsPathRooted(executable))
                {
                    if (!File.Exists(executable))
                        return false;

                    EnsureExecutablePermissions(executable);
                }

                var result = await RunProcessAsync(executable, ["--version"], ct);
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> DownloadManagedBinaryAsync(CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            {
                throw new InvalidOperationException(
                    "Automatic yt-dlp provisioning is currently supported only on Windows and Linux. Install yt-dlp manually and set COVE_YTDLP_PATH or YT_DLP_PATH.");
            }

            var (url, fileName) = OperatingSystem.IsWindows()
                ? ("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", "yt-dlp.exe")
                : ("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp", "yt-dlp");

            var toolsDir = Path.Combine(extensionRoot, "tools");
            Directory.CreateDirectory(toolsDir);

            var finalPath = Path.Combine(toolsDir, fileName);
            var tempPath = Path.Combine(toolsDir, fileName + ".tmp");

            logger.LogInformation("yt-dlp was not found on PATH. Downloading a managed copy for extension {ExtensionId} to {Path}", extensionId, finalPath);

            try
            {
                using var client = httpClientFactory.CreateClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output, ct);
                }

                EnsureExecutablePermissions(tempPath);

                if (File.Exists(finalPath))
                    File.Delete(finalPath);

                File.Move(tempPath, finalPath);
                EnsureExecutablePermissions(finalPath);

                if (!await IsUsableAsync(finalPath, ct))
                    throw new InvalidOperationException($"yt-dlp was downloaded to '{finalPath}' but could not be executed.");

                return finalPath;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    "yt-dlp is not installed and the extension could not download its managed copy. Install yt-dlp on PATH, set COVE_YTDLP_PATH / YT_DLP_PATH, or in Docker bake yt-dlp into the image or allow the extension directory to persist a downloaded copy.",
                    ex);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void EnsureExecutablePermissions(string filePath)
        {
            if (OperatingSystem.IsWindows() || !File.Exists(filePath))
                return;

            File.SetUnixFileMode(
                filePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
