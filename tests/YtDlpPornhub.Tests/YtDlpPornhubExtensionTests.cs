using Cove.Core.DTOs;
using Cove.Extensions.YtDlpPornhub;
using Cove.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Extensions.YtDlpPornhub.Tests;

public class YtDlpPornhubExtensionTests
{
    [Fact]
    public async Task MatchAsync_ReturnsCanonicalPornhubMatchWithQualityOptions()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => Task.FromResult(new YtDlpPornhubExtension.YtDlpCommandResult(0, SampleMetadataJson, string.Empty))
        };
        var extension = new YtDlpPornhubExtension(runner);

        var match = await extension.MatchAsync("https://www.pornhub.com/view_video.php?viewkey=abc123", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("builtin.ytdlp/pornhub-scene", match!.DownloaderId);
        Assert.Equal("https://www.pornhub.com/view_video.php?viewkey=abc123", match.NormalizedUrl);
        Assert.Equal("Test Scene Title", match.Label);
        Assert.Collection(
            Assert.IsAssignableFrom<IReadOnlyList<DownloaderQualityOption>>(match.QualityOptions),
            option => Assert.Equal("best", option.Id),
            option => Assert.Equal("max-height-1080", option.Id),
            option => Assert.Equal("max-height-720", option.Id),
            option => Assert.Equal("max-height-480", option.Id));
    }

    [Fact]
    public async Task DownloadAsync_UsesRequestedQualityAndReturnsDownloadedFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "cove-ytdlp-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var runner = new FakeRunner
            {
                Handler = (arguments, _) =>
                {
                    if (arguments.Contains("--dump-single-json"))
                    {
                        return Task.FromResult(new YtDlpPornhubExtension.YtDlpCommandResult(0, SampleMetadataJson, string.Empty));
                    }

                    var outputIndex = arguments.ToList().IndexOf("--output");
                    Assert.True(outputIndex >= 0);
                    var outputTemplate = arguments[outputIndex + 1];
                    File.WriteAllText(outputTemplate.Replace("%(ext)s", "mp4"), "video payload");
                    return Task.FromResult(new YtDlpPornhubExtension.YtDlpCommandResult(0, string.Empty, string.Empty));
                }
            };
            var extension = new YtDlpPornhubExtension(runner);
            var host = new FakeDownloaderHost(tempDirectory);

            var result = await extension.DownloadAsync(
                new DownloaderRequest(
                    "builtin.ytdlp/pornhub-scene",
                    "https://www.pornhub.com/view_video.php?viewkey=abc123",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["www.pornhub.com"]),
                    "max-height-720"),
                host,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("downloaded.mp4", result!.LocalPath);
            Assert.Equal("Test Scene Title [abc123].mp4", result.OriginalFilename);
            Assert.NotNull(result.InlineSceneMetadata);
            Assert.Equal("Test Scene Title", result.InlineSceneMetadata!.Title);
            Assert.Equal("abc123", result.InlineSceneMetadata.Code);
            Assert.Equal("2025-01-14", result.InlineSceneMetadata.Date);
            Assert.Equal("Test Creator", Assert.Single(result.InlineSceneMetadata.PerformerNames));
            Assert.Contains("tag one", result.InlineSceneMetadata.TagNames);
            Assert.Contains(runner.Calls, arguments => arguments.Contains("best[height<=720]/best"));
            Assert.Equal(0.95d, host.ProgressValues.Max());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ScrapeSceneAsync_ReturnsMetadataFromYtDlp()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => Task.FromResult(new YtDlpPornhubExtension.YtDlpCommandResult(0, SampleMetadataJson, string.Empty))
        };
        var extension = new YtDlpPornhubExtension(runner);

        var scraped = await extension.ScrapeSceneAsync(
            new ScraperRequest<SceneScrapeInput>(
                "builtin.ytdlp/pornhub-scene-metadata",
                new SceneScrapeInput { Url = "https://www.pornhub.com/view_video.php?viewkey=abc123" },
                new ScraperPermissions(["www.pornhub.com"])),
            CancellationToken.None);

        Assert.NotNull(scraped);
        Assert.Equal("Test Scene Title", scraped!.Title);
        Assert.Equal("abc123", scraped.Code);
        Assert.Equal("Test description", scraped.Details);
        Assert.Equal("2025-01-14", scraped.Date);
        Assert.Equal("https://cdn.example.test/thumb.jpg", scraped.ImageUrl);
        Assert.Equal("Test Creator", Assert.Single(scraped.PerformerNames));
        Assert.Contains("tag one", scraped.TagNames);
        Assert.Contains("https://www.pornhub.com/view_video.php?viewkey=abc123", scraped.Urls);
    }

    [Fact]
    public async Task ScrapeSceneAsync_UsesSceneCodeWhenUrlIsMissing()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => Task.FromResult(new YtDlpPornhubExtension.YtDlpCommandResult(0, SampleMetadataJson, string.Empty))
        };
        var extension = new YtDlpPornhubExtension(runner);

        var scraped = await extension.ScrapeSceneAsync(
            new ScraperRequest<SceneScrapeInput>(
                "builtin.ytdlp/pornhub-scene-metadata",
                new SceneScrapeInput { Code = "abc123" },
                new ScraperPermissions(["www.pornhub.com"])),
            CancellationToken.None);

        Assert.NotNull(scraped);
        Assert.Contains(runner.Calls, arguments => arguments.Contains("https://www.pornhub.com/view_video.php?viewkey=abc123"));
    }

    private const string SampleMetadataJson = """
{
  "id": "abc123",
  "description": "Test description",
  "thumbnail": "https://cdn.example.test/thumb.jpg",
  "title": "Test Scene Title",
  "uploader": "Test Creator",
  "upload_date": "20250114",
  "tags": ["tag one", "tag two"],
  "webpage_url": "https://www.pornhub.com/view_video.php?viewkey=abc123",
  "formats": [
    { "format_id": "1080p", "height": 1080, "vcodec": "avc1" },
    { "format_id": "720p", "height": 720, "vcodec": "avc1" },
    { "format_id": "480p", "height": 480, "vcodec": "avc1" },
    { "format_id": "audio-only", "height": 0, "vcodec": "none" }
  ]
}
""";

    private sealed class FakeRunner : YtDlpPornhubExtension.IYtDlpCommandRunner
    {
        public required Func<IReadOnlyList<string>, CancellationToken, Task<YtDlpPornhubExtension.YtDlpCommandResult>> Handler { get; init; }
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<YtDlpPornhubExtension.YtDlpCommandResult> RunAsync(IEnumerable<string> arguments, CancellationToken ct)
        {
            var capturedArguments = arguments.ToList();
            Calls.Add(capturedArguments);
            return Handler(capturedArguments, ct);
        }
    }

    private sealed class FakeDownloaderHost(string tempDirectory) : IDownloaderHost
    {
        public string TempDirectory { get; } = tempDirectory;
        public IHttpClientFactory HttpClients => throw new NotSupportedException();
        public List<double> ProgressValues { get; } = [];

        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void ReportProgress(double progress, string? message = null)
        {
            ProgressValues.Add(progress);
        }
    }
}
