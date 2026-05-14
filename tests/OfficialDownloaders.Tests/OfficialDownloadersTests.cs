using System.Net;
using Cove.Core.DTOs;
using Cove.Extensions.OfficialDownloaders;
using Cove.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Extensions.OfficialDownloaders.Tests;

public class OfficialDownloadersTests
{
    [Fact]
    public async Task YtDlp_InitializeAsync_ResolvesExtensionRoot_WhenHotLoadedWithoutConfigureServices()
    {
        var extensionsDataDir = Path.Combine(Path.GetTempPath(), "cove-ytdlp-ext-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(extensionsDataDir);

        try
        {
            var configuration = new ConfigurationBuilder().Build();
            var extensionManager = new ExtensionManager(new ExtensionContext
            {
                Configuration = configuration,
                DataDirectory = extensionsDataDir,
                CoveVersion = "0.0.1"
            });

            var extension = new YtDlpDownloaderExtension(new FakeRunner
            {
                Handler = (_, _) => Task.FromResult(new YtDlpDownloaderExtension.YtDlpCommandResult(0, SampleVideoMetadataJson, string.Empty))
            });

            await using var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(extensionManager)
                .AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
                .BuildServiceProvider();

            await extension.InitializeAsync(services, CancellationToken.None);

            Assert.True(Directory.Exists(Path.Combine(extensionsDataDir, extension.Id)));
        }
        finally
        {
            if (Directory.Exists(extensionsDataDir))
                Directory.Delete(extensionsDataDir, recursive: true);
        }
    }

    [Fact]
    public async Task YtDlp_MatchAsync_ReturnsVideoMatchWithQualityOptions()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => Task.FromResult(new YtDlpDownloaderExtension.YtDlpCommandResult(0, SampleVideoMetadataJson, string.Empty))
        };
        var extension = new YtDlpDownloaderExtension(runner);

        var match = await extension.MatchAsync("https://video.example.test/watch/abc123", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("cove.official.downloaders.ytdlp/video", match!.DownloaderId);
        Assert.Equal("https://video.example.test/watch/abc123", match.NormalizedUrl);
        Assert.Equal("Test Scene Title", match.Label);
        Assert.Collection(
            Assert.IsAssignableFrom<IReadOnlyList<DownloaderQualityOption>>(match.QualityOptions),
            option => Assert.Equal("best", option.Id),
            option => Assert.Equal("max-height-1080", option.Id),
            option => Assert.Equal("max-height-720", option.Id),
            option => Assert.Equal("max-height-480", option.Id));
    }

    [Fact]
    public async Task YtDlp_MatchAsync_DoesNotClaimCommonAudioSites()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => throw new InvalidOperationException("yt-dlp should not be called for common audio sites")
        };
        var extension = new YtDlpDownloaderExtension(runner);

        var match = await extension.MatchAsync("https://soundgasm.net/u/artist/story", CancellationToken.None);

        Assert.Null(match);
    }

    [Fact]
    public async Task YtDlp_DownloadAsync_UsesRequestedQualityAndReturnsDownloadedFile()
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
                        return Task.FromResult(new YtDlpDownloaderExtension.YtDlpCommandResult(0, SampleVideoMetadataJson, string.Empty));

                    var outputIndex = arguments.ToList().IndexOf("--output");
                    Assert.True(outputIndex >= 0);
                    var outputTemplate = arguments[outputIndex + 1];
                    File.WriteAllText(outputTemplate.Replace("%(ext)s", "mp4"), "video payload");
                    return Task.FromResult(new YtDlpDownloaderExtension.YtDlpCommandResult(0, string.Empty, string.Empty));
                }
            };
            var extension = new YtDlpDownloaderExtension(runner);
            var host = new FakeDownloaderHost(tempDirectory);

            var result = await extension.DownloadAsync(
                new DownloaderRequest(
                    "cove.official.downloaders.ytdlp/video",
                    "https://video.example.test/watch/abc123",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["video.example.test"]),
                    "max-height-720"),
                host,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("downloaded.mp4", result!.LocalPath);
            Assert.Equal("Test Scene Title [abc123].mp4", result.OriginalFilename);
            Assert.NotNull(result.InlineSceneMetadata);
            Assert.Equal("Test Scene Title", result.InlineSceneMetadata!.Title);
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
    public async Task CommonAudio_MatchAndDownload_SoundgasmPage()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "cove-audio-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var factory = new FakeHttpClientFactory(request =>
            {
                if (request.RequestUri?.Host == "soundgasm.net")
                {
                    return HtmlResponse("""
<html><head><meta property="og:title" content="Audio Title"><meta property="og:audio" content="https://media.example.test/audio.mp3"></head><body></body></html>
""");
                }

                if (request.RequestUri?.Host == "media.example.test")
                    return BytesResponse("audio payload");

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var extension = new CommonAudioDownloaderExtension();
            await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

            var match = await extension.MatchAsync("https://soundgasm.net/u/artist/story", CancellationToken.None);
            Assert.NotNull(match);
            Assert.Equal("cove.official.downloaders.common-audio/audio", match!.DownloaderId);
            Assert.Equal("Audio Title", match.Label);

            var result = await extension.DownloadAsync(
                new DownloaderRequest(
                    "cove.official.downloaders.common-audio/audio",
                    "https://soundgasm.net/u/artist/story",
                    DownloaderEntity.Audio,
                    new DownloaderPermissions(["soundgasm.net"])),
                new FakeDownloaderHost(tempDirectory),
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("downloaded.mp3", result!.LocalPath);
            Assert.Equal("Audio Title.mp3", result.OriginalFilename);
            Assert.True(File.Exists(Path.Combine(tempDirectory, result.LocalPath)));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CommonAudio_ScrapeAudio_ExtractsBracketTagsFromTitleAndDescription()
    {
        const string sourceUrl = "https://soundgasm.net/u/artist/story";
        var factory = new FakeHttpClientFactory(_ => HtmlResponse("""
<html>
    <head><meta property="og:audio" content="https://media.example.test/audio.mp3"></head>
    <body>
        <div class="jp-title">[F4M] Clean Audio [Whispering]</div>
        <div class="jp-description">Intro [Kissing] body [Slow Burn]</div>
    </body>
</html>
"""));
        var extension = new CommonAudioDownloaderExtension();
        await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

        var result = await extension.ScrapeAudioAsync(
            new ScraperRequest<AudioScrapeInput>(
                "cove.official.downloaders.common-audio/scraper",
                new AudioScrapeInput { Url = sourceUrl },
                new ScraperPermissions(["soundgasm.net"])),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Clean Audio", result!.Title);
        Assert.Equal("Intro body", result.Details);
        Assert.Equal(["F4M", "Whispering", "Kissing", "Slow Burn"], result.TagNames);
    }

    [Fact]
    public async Task CommonAudio_DownloadsDirectAudioUrlFoundInPageBody()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "cove-audio-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var factory = new FakeHttpClientFactory(request =>
            {
                if (request.RequestUri?.Host == "whyp.it")
                {
                    return HtmlResponse("""
<html><head><title>Direct Audio Title</title></head><body>https://media.example.test/direct-audio.m4a</body></html>
""");
                }

                if (request.RequestUri?.Host == "media.example.test")
                    return BytesResponse("audio payload");

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var extension = new CommonAudioDownloaderExtension();
            await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

            var result = await extension.DownloadAsync(
                new DownloaderRequest(
                    "cove.official.downloaders.common-audio/audio",
                    "https://whyp.it/item/direct-audio",
                    DownloaderEntity.Audio,
                    new DownloaderPermissions(["whyp.it"])),
                new FakeDownloaderHost(tempDirectory),
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("downloaded.m4a", result!.LocalPath);
            Assert.Equal("Direct Audio Title.m4a", result.OriginalFilename);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Reddit_MatchAll_ReturnsNativeMediaAndDivertsExternalLinks()
    {
        const string sourceUrl = "https://www.reddit.com/r/gonewildaudio/comments/abc/example_post";
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
[
  {
    "data": {
      "children": [
        {
          "data": {
            "title": "Forum post title",
            "url_overridden_by_dest": "https://i.redd.it/native-image.jpg",
            "secure_media": { "reddit_video": { "fallback_url": "https://v.redd.it/abc/DASH_720.mp4" } },
            "selftext": "Listen here: https://soundgasm.net/u/artist/story"
          }
        }
      ]
    }
  }
]
"""));
        var extension = new RedditDownloaderExtension();
        await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

        var matches = await extension.MatchAllAsync(sourceUrl, CancellationToken.None);

        Assert.Collection(
            matches,
            match =>
            {
                Assert.Equal("cove.official.downloaders.reddit/image", match.DownloaderId);
                Assert.Equal("https://i.redd.it/native-image.jpg", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.False(match.Divert);
            },
            match =>
            {
                Assert.Equal("cove.official.downloaders.reddit/video", match.DownloaderId);
                Assert.Equal("https://v.redd.it/abc/DASH_720.mp4", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.False(match.Divert);
            },
            match =>
            {
                Assert.Equal("cove.official.downloaders.reddit/divert", match.DownloaderId);
                Assert.Equal("https://soundgasm.net/u/artist/story", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.True(match.Divert);
            });
    }

        [Fact]
        public async Task Reddit_Scrapers_ExtractBracketTagsFromTitleAndDescription()
        {
                const string sourceUrl = "https://www.reddit.com/r/gonewildaudio/comments/abc/example_post";
                var factory = new FakeHttpClientFactory(_ => JsonResponse("""
[
    {
        "data": {
            "children": [
                {
                    "data": {
                        "title": "[F4M] Story Title [ASMR]",
                        "url_overridden_by_dest": "https://i.redd.it/native-image.jpg",
                        "secure_media": { "reddit_video": { "fallback_url": "https://v.redd.it/abc/DASH_720.mp4" } },
                        "selftext": "Listen here [Kissing] https://soundgasm.net/u/artist/story and [Slow Burn]"
                    }
                }
            ]
        }
    }
]
"""));
                var extension = new RedditDownloaderExtension();
                await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

                var audio = await extension.ScrapeAudioAsync(
                        new ScraperRequest<AudioScrapeInput>(
                                "cove.official.downloaders.reddit/scraper-audio",
                                new AudioScrapeInput { Url = sourceUrl },
                                new ScraperPermissions(["www.reddit.com"])),
                        CancellationToken.None);
                var image = await extension.ScrapeImageAsync(
                        new ScraperRequest<ImageScrapeInput>(
                                "cove.official.downloaders.reddit/scraper-image",
                                new ImageScrapeInput { Url = sourceUrl },
                                new ScraperPermissions(["www.reddit.com"])),
                        CancellationToken.None);
                var scene = await extension.ScrapeSceneAsync(
                        new ScraperRequest<SceneScrapeInput>(
                                "cove.official.downloaders.reddit/scraper-scene",
                                new SceneScrapeInput { Url = sourceUrl },
                                new ScraperPermissions(["www.reddit.com"])),
                        CancellationToken.None);

                Assert.NotNull(audio);
                Assert.NotNull(image);
                Assert.NotNull(scene);
                AssertRedditBracketMetadata(audio!.Title, audio.Details, audio.TagNames);
                AssertRedditBracketMetadata(image!.Title, image.Details, image.TagNames);
                AssertRedditBracketMetadata(scene!.Title, scene.Details, scene.TagNames);
        }

        [Fact]
        public async Task Reddit_MatchAll_ResolvesRedgifsLinksToVideoMatch()
        {
                const string sourceUrl = "https://www.reddit.com/r/example/comments/abc/redgifs_post";
                var factory = new FakeHttpClientFactory(request =>
                {
                        if (request.RequestUri?.Host == "www.reddit.com")
                        {
                                return JsonResponse("""
[
    {
        "data": {
            "children": [
                {
                    "data": {
                        "title": "Redgifs post title",
                        "selftext": "Watch here: https://www.redgifs.com/watch/exampleclip"
                    }
                }
            ]
        }
    }
]
""");
                        }

                        if (request.RequestUri?.AbsoluteUri == "https://api.redgifs.com/v2/auth/temporary")
                                return JsonResponse("""{ "token": "temporary-token" }""");

                        if (request.RequestUri?.AbsoluteUri == "https://api.redgifs.com/v2/gifs/exampleclip")
                                return JsonResponse("""{ "gif": { "urls": { "hd": "https://media.redgifs.com/ExampleClip.mp4" } } }""");

                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                });
                var extension = new RedditDownloaderExtension();
                await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

                var match = Assert.Single(await extension.MatchAllAsync(sourceUrl, CancellationToken.None));

                Assert.Equal("cove.official.downloaders.reddit/video", match.DownloaderId);
                Assert.Equal("https://media.redgifs.com/ExampleClip.mp4", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.False(match.Divert);
        }

        [Fact]
        public async Task Reddit_MatchAll_ToleratesNullMediaFields()
        {
                const string sourceUrl = "https://www.reddit.com/r/gonewildaudio/comments/abc/example_post";
                var factory = new FakeHttpClientFactory(_ => JsonResponse("""
[
    {
        "data": {
            "children": [
                {
                    "data": {
                        "title": "Audio post title",
                        "secure_media": null,
                        "media": null,
                        "selftext": "Listen here: https://hotaudio.net/u/artist/story"
                    }
                }
            ]
        }
    }
]
"""));
                var extension = new RedditDownloaderExtension();
                await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

                var matches = await extension.MatchAllAsync(sourceUrl, CancellationToken.None);

                var match = Assert.Single(matches);
                Assert.Equal("cove.official.downloaders.reddit/divert", match.DownloaderId);
                Assert.Equal("https://hotaudio.net/u/artist/story", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.True(match.Divert);
        }

        [Fact]
        public async Task Reddit_MatchAll_ReturnsGalleryImagesWhenMediaFieldsAreNull()
        {
                const string sourceUrl = "https://www.reddit.com/r/pics/comments/abc/gallery_post";
                var factory = new FakeHttpClientFactory(_ => JsonResponse("""
[
    {
        "data": {
            "children": [
                {
                    "data": {
                        "title": "Gallery post title",
                        "secure_media": null,
                        "media": null,
                        "gallery_data": { "items": [ { "media_id": "one" } ] },
                        "media_metadata": {
                            "one": { "s": { "u": "https://preview.redd.it/image-one.jpg?width=1000&amp;format=pjpg&amp;auto=webp" } }
                        }
                    }
                }
            ]
        }
    }
]
"""));
                var extension = new RedditDownloaderExtension();
                await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

                var matches = await extension.MatchAllAsync(sourceUrl, CancellationToken.None);

                var match = Assert.Single(matches);
                Assert.Equal("cove.official.downloaders.reddit/image", match.DownloaderId);
                Assert.Equal("https://preview.redd.it/image-one.jpg?width=1000&format=pjpg&auto=webp", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.False(match.Divert);
        }

    [Fact]
    public async Task CommonText_DownloadsLiteroticaStoryToTextFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "cove-text-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
                        var factory = new FakeHttpClientFactory(_ => HtmlResponse("""
<html>
    <head>
        <title>The Bracelet - Taboo/Incest - Literotica.com</title>
        <script type="application/ld+json">
            {
                "@context": "https://schema.org",
                "@type": "Article",
                "headline": "The Bracelet",
                "author": { "@type": "Person", "name": "nolimits" }
            }
        </script>
    </head>
    <body>
        <a href="/authors/webcams">Try the free LITEROTICA WEBCAMS!</a>
        <article class="content space_sm">
            <div class="core core_custom">
                <div class="_tab__content_8l2r4_875">Story Info Granddad passes on bracelet to grandson.</div>
                <p class="_files__text_8l2r4_731">Log in to change and save custom font settings</p>
                <p>November 2025 update is live! We are improving site performance and adding new features based on your feedback.</p>
                <p>Switch back to Classic.</p>
                <p>Help us make Literotica better, let us know what you think.</p>
                <div class="_article__content_10cj1_81">
                    <p>First paragraph.</p>
                    <p>Second paragraph with <em>inline</em> formatting.</p>
                    <p>Third paragraph.</p>
                </div>
            </div>
        </article>
    </body>
</html>
"""));
            var extension = new CommonTextDownloaderExtension();
            await extension.InitializeAsync(BuildServices(factory), CancellationToken.None);

            var match = await extension.MatchAsync("https://www.literotica.com/s/story-title", CancellationToken.None);
            Assert.NotNull(match);
            Assert.Equal("cove.official.downloaders.common-text/literotica", match!.DownloaderId);
            Assert.Equal("The Bracelet - Taboo/Incest", match.Label);

            var result = await extension.DownloadAsync(
                new DownloaderRequest(
                    "cove.official.downloaders.common-text/literotica",
                    "https://www.literotica.com/s/story-title",
                    DownloaderEntity.Text,
                    new DownloaderPermissions(["www.literotica.com"])),
                new FakeDownloaderHost(tempDirectory),
                CancellationToken.None);

            Assert.NotNull(result);
            var content = await File.ReadAllTextAsync(Path.Combine(tempDirectory, result!.LocalPath));
            Assert.Equal("The Bracelet - Taboo_Incest.txt", result.OriginalFilename);
            Assert.StartsWith("First paragraph.", content);
            Assert.Contains($"First paragraph.{Environment.NewLine}{Environment.NewLine}Second paragraph with inline formatting.{Environment.NewLine}{Environment.NewLine}Third paragraph.", content);
            Assert.DoesNotContain("Try the free LITEROTICA WEBCAMS!", content);
            Assert.DoesNotContain("Story Info", content);
            Assert.DoesNotContain("Log in to change", content);
            Assert.DoesNotContain("November 2025 update", content);
            Assert.DoesNotContain("Switch back to Classic", content);
            Assert.DoesNotContain("Help us make Literotica better", content);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private const string SampleVideoMetadataJson = """
{
  "id": "abc123",
  "description": "Test description",
  "thumbnail": "https://cdn.example.test/thumb.jpg",
  "title": "Test Scene Title",
  "uploader": "Test Creator",
  "upload_date": "20250114",
  "tags": ["tag one", "tag two"],
  "webpage_url": "https://video.example.test/watch/abc123",
  "formats": [
    { "format_id": "1080p", "height": 1080, "vcodec": "avc1", "acodec": "mp4a" },
    { "format_id": "720p", "height": 720, "vcodec": "avc1", "acodec": "mp4a" },
    { "format_id": "480p", "height": 480, "vcodec": "avc1", "acodec": "mp4a" },
    { "format_id": "audio-only", "height": 0, "vcodec": "none", "acodec": "mp4a" }
  ]
}
""";

    private static ServiceProvider BuildServices(IHttpClientFactory httpClientFactory)
    {
        return new ServiceCollection()
            .AddSingleton(httpClientFactory)
            .BuildServiceProvider();
    }

    private static HttpResponseMessage HtmlResponse(string html)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private static HttpResponseMessage BytesResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };
    }

    private static void AssertRedditBracketMetadata(string? title, string? details, IReadOnlyList<string> tagNames)
    {
        Assert.Equal("Story Title", title);
        Assert.Equal("Listen here https://soundgasm.net/u/artist/story and", details);
        Assert.Equal(["F4M", "ASMR", "Kissing", "Slow Burn"], tagNames);
    }

    private sealed class FakeRunner : YtDlpDownloaderExtension.IYtDlpCommandRunner
    {
        public required Func<IReadOnlyList<string>, CancellationToken, Task<YtDlpDownloaderExtension.YtDlpCommandResult>> Handler { get; init; }
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<YtDlpDownloaderExtension.YtDlpCommandResult> RunAsync(IEnumerable<string> arguments, CancellationToken ct)
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

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeHttpMessageHandler(handler));
        }
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
