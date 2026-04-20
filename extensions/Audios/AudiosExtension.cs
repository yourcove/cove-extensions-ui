using System.Security.Cryptography;
using System.Text.Json;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Cove.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TagLib;

namespace Cove.Extensions.Audios;

public class AudiosExtension : FullExtensionBase
{
    public override string Id => "cove.official.audios";
    public override string Name => "Audios";
    public override string Version => "2.0.0";
    public override string? Description => "Full audio file management — browse, play, organize, tag, and filter audio files in your library.";
    public override string? Author => "Cove Team";
    public override string? Url => "https://github.com/yourcove/cove-extensions-ui";
    public override IReadOnlyList<string> Categories => [ExtensionCategories.ContentManagement, ExtensionCategories.MediaPlayer, ExtensionCategories.Library];

    private IServiceProvider? _services;

    public override Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        _services = services;

        // Ensure "audios" is in the nav menu items so the page shows by default
        var cfg = services.GetRequiredService<IOptions<CoveConfiguration>>().Value;
        if (!cfg.Interface.MenuItems.Contains("audios", StringComparer.OrdinalIgnoreCase))
            cfg.Interface.MenuItems.Add("audios");

        return Task.CompletedTask;
    }

    public override Task OnInstallAsync(IServiceProvider services, CancellationToken ct = default)
        => Task.CompletedTask;

    // ── Data ────────────────────────────────────────────────────────

    public override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AudioEntity>(e =>
        {
            e.ToTable("ext_audios");
            e.HasKey(a => a.Id);
            e.Property(a => a.Title).HasMaxLength(500);
            e.Property(a => a.Path).HasMaxLength(2000);
            e.Property(a => a.Artist).HasMaxLength(500);
            e.Property(a => a.Album).HasMaxLength(500);
            e.Property(a => a.Genre).HasMaxLength(200);
            e.Property(a => a.Date).HasMaxLength(10);
            e.Property(a => a.Checksum).HasMaxLength(128);
            e.HasIndex(a => a.Path).IsUnique();
            e.HasIndex(a => a.Checksum);
        });

        modelBuilder.Entity<AudioTagLink>(e =>
        {
            e.ToTable("ext_audio_tags");
            e.HasKey(at => new { at.AudioId, at.TagId });
            e.HasIndex(at => at.TagId);
        });

        modelBuilder.Entity<AudioPerformerLink>(e =>
        {
            e.ToTable("ext_audio_performers");
            e.HasKey(ap => new { ap.AudioId, ap.PerformerId });
            e.HasIndex(ap => ap.PerformerId);
        });

        modelBuilder.Entity<AudioGroupLink>(e =>
        {
            e.ToTable("ext_audio_groups");
            e.HasKey(ag => new { ag.AudioId, ag.GroupId });
            e.HasIndex(ag => ag.GroupId);
        });
    }

    protected override void DefineMigrations()
    {
        Migration("001_create_audios_table", """
            CREATE TABLE IF NOT EXISTS ext_audios (
                "Id" SERIAL PRIMARY KEY,
                "Title" VARCHAR(500) NOT NULL,
                "Path" VARCHAR(2000) NOT NULL UNIQUE,
                "Artist" VARCHAR(500),
                "Album" VARCHAR(500),
                "Genre" VARCHAR(200),
                "Duration" DOUBLE PRECISION,
                "Bitrate" INTEGER,
                "SampleRate" INTEGER,
                "Channels" INTEGER,
                "FileSize" BIGINT,
                "Checksum" VARCHAR(128),
                "CoverImagePath" TEXT,
                "TrackNumber" INTEGER,
                "Year" INTEGER,
                "Rating" INTEGER DEFAULT 0,
                "PlayCount" INTEGER DEFAULT 0,
                "LastPlayed" TIMESTAMP WITH TIME ZONE,
                "Organized" BOOLEAN DEFAULT FALSE,
                "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                "UpdatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            );
            """);

        Migration("002_create_audio_tags_table", """
            CREATE TABLE IF NOT EXISTS ext_audio_tags (
                "AudioId" INTEGER NOT NULL REFERENCES ext_audios("Id") ON DELETE CASCADE,
                "TagId" INTEGER NOT NULL,
                PRIMARY KEY ("AudioId", "TagId")
            );
            CREATE INDEX IF NOT EXISTS ix_ext_audio_tags_tagid ON ext_audio_tags ("TagId");
            """);

        Migration("003_add_performers_groups_date", """
            CREATE TABLE IF NOT EXISTS ext_audio_performers (
                "AudioId" INTEGER NOT NULL REFERENCES ext_audios("Id") ON DELETE CASCADE,
                "PerformerId" INTEGER NOT NULL,
                PRIMARY KEY ("AudioId", "PerformerId")
            );
            CREATE INDEX IF NOT EXISTS ix_ext_audio_performers_performerid ON ext_audio_performers ("PerformerId");

            CREATE TABLE IF NOT EXISTS ext_audio_groups (
                "AudioId" INTEGER NOT NULL REFERENCES ext_audios("Id") ON DELETE CASCADE,
                "GroupId" INTEGER NOT NULL,
                PRIMARY KEY ("AudioId", "GroupId")
            );
            CREATE INDEX IF NOT EXISTS ix_ext_audio_groups_groupid ON ext_audio_groups ("GroupId");

            ALTER TABLE ext_audios ADD COLUMN IF NOT EXISTS "Date" VARCHAR(10);
            ALTER TABLE ext_audios ADD COLUMN IF NOT EXISTS "StudioId" INTEGER;
            """);
    }

    // ── API ─────────────────────────────────────────────────────────

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ext/audios");

        // ── List ────────────────────────────────────────────────────
        group.MapGet("/", async (
            HttpContext ctx,
            int page = 1,
            int perPage = 40,
            string sort = "title",
            string direction = "asc",
            string? q = null,
            string? genre = null,
            int? tagId = null,
            int? performerId = null,
            int? groupId = null,
            int? studioId = null,
            int? ratingGt = null,
            bool? organized = null,
            string? filters = null) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            IQueryable<AudioEntity> query = db.Set<AudioEntity>();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.ToLower();
                query = query.Where(a => a.Title.ToLower().Contains(search) ||
                    (a.Artist != null && a.Artist.ToLower().Contains(search)) ||
                    (a.Album != null && a.Album.ToLower().Contains(search)) ||
                    (a.Genre != null && a.Genre.ToLower().Contains(search)));
            }
            if (!string.IsNullOrWhiteSpace(genre))
                query = query.Where(a => a.Genre != null && a.Genre.ToLower().Contains(genre.ToLower()));
            if (organized.HasValue)
                query = query.Where(a => a.Organized == organized.Value);
            if (ratingGt.HasValue)
                query = query.Where(a => a.Rating > ratingGt.Value);
            if (studioId.HasValue)
                query = query.Where(a => a.StudioId == studioId.Value);

            if (tagId.HasValue)
            {
                var audioIds = db.Set<AudioTagLink>()
                    .Where(t => t.TagId == tagId.Value)
                    .Select(t => t.AudioId);
                query = query.Where(a => audioIds.Contains(a.Id));
            }
            if (performerId.HasValue)
            {
                var audioIds = db.Set<AudioPerformerLink>()
                    .Where(p => p.PerformerId == performerId.Value)
                    .Select(p => p.AudioId);
                query = query.Where(a => audioIds.Contains(a.Id));
            }
            if (groupId.HasValue)
            {
                var audioIds = db.Set<AudioGroupLink>()
                    .Where(g => g.GroupId == groupId.Value)
                    .Select(g => g.AudioId);
                query = query.Where(a => audioIds.Contains(a.Id));
            }

            if (!string.IsNullOrWhiteSpace(filters))
                query = ApplyAdvancedFilters(query, db, filters);

            if (sort.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(a => EF.Functions.Random());
            }
            else
            {
                query = (sort.ToLower(), direction.ToLower()) switch
                {
                    ("title", "desc") => query.OrderByDescending(a => a.Title),
                    ("duration", "asc") => query.OrderBy(a => a.Duration),
                    ("duration", "desc") => query.OrderByDescending(a => a.Duration),
                    ("date", "asc") => query.OrderBy(a => a.Date ?? ""),
                    ("date", "desc") => query.OrderByDescending(a => a.Date ?? ""),
                    ("rating", "asc") => query.OrderBy(a => a.Rating),
                    ("rating", "desc") => query.OrderByDescending(a => a.Rating),
                    ("playcount", "asc") => query.OrderBy(a => a.PlayCount),
                    ("playcount", "desc") => query.OrderByDescending(a => a.PlayCount),
                    ("file_size", "asc") => query.OrderBy(a => a.FileSize),
                    ("file_size", "desc") => query.OrderByDescending(a => a.FileSize),
                    ("created_at", "asc") => query.OrderBy(a => a.CreatedAt),
                    ("created_at", "desc") => query.OrderByDescending(a => a.CreatedAt),
                    ("updated_at", "asc") => query.OrderBy(a => a.UpdatedAt),
                    ("updated_at", "desc") => query.OrderByDescending(a => a.UpdatedAt),
                    _ => query.OrderBy(a => a.Title),
                };
            }

            var totalCount = await query.CountAsync();
            var pageIds = await query
                .Skip((page - 1) * perPage)
                .Take(perPage)
                .Select(a => a.Id)
                .ToListAsync();

            var tagCounts = await db.Set<AudioTagLink>()
                .Where(t => pageIds.Contains(t.AudioId))
                .GroupBy(t => t.AudioId)
                .Select(g => new { AudioId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AudioId, x => x.Count);

            // Resolve tag names for the page
            var tagLinks = await db.Set<AudioTagLink>()
                .Where(t => pageIds.Contains(t.AudioId))
                .ToListAsync();
            var tagIdSet = tagLinks.Select(t => t.TagId).Distinct().ToList();
            var tagNames = await db.Set<Cove.Core.Entities.Tag>()
                .Where(t => tagIdSet.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToDictionaryAsync(t => t.Id, t => t.Name);
            var audioTags = tagLinks
                .GroupBy(t => t.AudioId)
                .ToDictionary(g => g.Key, g => g.Select(t => new EntityRefDto { Id = t.TagId, Name = tagNames.GetValueOrDefault(t.TagId, "") }).ToList());

            var performerCounts = await db.Set<AudioPerformerLink>()
                .Where(p => pageIds.Contains(p.AudioId))
                .GroupBy(p => p.AudioId)
                .Select(g => new { AudioId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AudioId, x => x.Count);

            // Resolve performer names + images for the page
            var perfLinks = await db.Set<AudioPerformerLink>()
                .Where(p => pageIds.Contains(p.AudioId))
                .ToListAsync();
            var perfIdSet = perfLinks.Select(p => p.PerformerId).Distinct().ToList();
            var perfInfos = await db.Set<Cove.Core.Entities.Performer>()
                .Where(p => perfIdSet.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, HasImage = p.ImageBlobId != null })
                .ToDictionaryAsync(p => p.Id);
            var audioPerformers = perfLinks
                .GroupBy(p => p.AudioId)
                .ToDictionary(g => g.Key, g => g.Select(p => {
                    var info = perfInfos.GetValueOrDefault(p.PerformerId);
                    return new EntityRefDto { Id = p.PerformerId, Name = info?.Name ?? "", ImagePath = info?.HasImage == true ? $"/api/performers/{p.PerformerId}/image" : null };
                }).ToList());

            var groupCounts = await db.Set<AudioGroupLink>()
                .Where(g => pageIds.Contains(g.AudioId))
                .GroupBy(g => g.AudioId)
                .Select(gr => new { AudioId = gr.Key, Count = gr.Count() })
                .ToDictionaryAsync(x => x.AudioId, x => x.Count);

            // Resolve group names for the page
            var groupLinks = await db.Set<AudioGroupLink>()
                .Where(g => pageIds.Contains(g.AudioId))
                .ToListAsync();
            var groupIdSet = groupLinks.Select(g => g.GroupId).Distinct().ToList();
            var groupNames = await db.Set<Cove.Core.Entities.Group>()
                .Where(g => groupIdSet.Contains(g.Id))
                .Select(g => new { g.Id, g.Name })
                .ToDictionaryAsync(g => g.Id, g => g.Name);
            var audioGroups = groupLinks
                .GroupBy(g => g.AudioId)
                .ToDictionary(g => g.Key, g => g.Select(gl => new EntityRefDto { Id = gl.GroupId, Name = groupNames.GetValueOrDefault(gl.GroupId, "") }).ToList());

            var audios = await db.Set<AudioEntity>()
                .Where(a => pageIds.Contains(a.Id))
                .ToListAsync();

            // Resolve studio names
            var studioIds = audios.Where(a => a.StudioId != null).Select(a => a.StudioId!.Value).Distinct().ToList();
            var studioNames = studioIds.Count > 0
                ? await db.Set<Cove.Core.Entities.Studio>()
                    .Where(s => studioIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.Name })
                    .ToDictionaryAsync(s => s.Id, s => s.Name)
                : new Dictionary<int, string>();

            var audioMap = audios.ToDictionary(a => a.Id);
            var items = pageIds
                .Where(id => audioMap.ContainsKey(id))
                .Select(id =>
                {
                    var a = audioMap[id];
                    return new AudioSummaryDto
                    {
                        Id = a.Id,
                        Title = a.Title,
                        Path = a.Path,
                        Genre = a.Genre,
                        Duration = a.Duration,
                        CoverImagePath = ResolveCoverImageUrl(a),
                        Rating = a.Rating,
                        PlayCount = a.PlayCount,
                        Date = a.Date,
                        Organized = a.Organized,
                        StudioId = a.StudioId,
                        StudioName = a.StudioId != null ? studioNames.GetValueOrDefault(a.StudioId.Value) : null,
                        TagCount = tagCounts.GetValueOrDefault(a.Id),
                        PerformerCount = performerCounts.GetValueOrDefault(a.Id),
                        GroupCount = groupCounts.GetValueOrDefault(a.Id),
                        Tags = audioTags.GetValueOrDefault(a.Id) ?? [],
                        Performers = audioPerformers.GetValueOrDefault(a.Id) ?? [],
                        Groups = audioGroups.GetValueOrDefault(a.Id) ?? [],
                    };
                })
                .ToList();

            return Results.Ok(new { items, totalCount });
        });

        // ── Get single ──────────────────────────────────────────────
        group.MapGet("/{id:int}", async (int id, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null) return Results.NotFound();

            var tagIds = await db.Set<AudioTagLink>()
                .Where(t => t.AudioId == id).Select(t => t.TagId).ToListAsync();
            var performerIds = await db.Set<AudioPerformerLink>()
                .Where(p => p.AudioId == id).Select(p => p.PerformerId).ToListAsync();
            var groupIds = await db.Set<AudioGroupLink>()
                .Where(g => g.AudioId == id).Select(g => g.GroupId).ToListAsync();

            return Results.Ok(new AudioDetailDto
            {
                Id = audio.Id,
                Title = audio.Title,
                Path = audio.Path,
                Artist = audio.Artist,
                Album = audio.Album,
                Genre = audio.Genre,
                Duration = audio.Duration,
                Bitrate = audio.Bitrate,
                SampleRate = audio.SampleRate,
                Channels = audio.Channels,
                FileSize = audio.FileSize,
                Checksum = audio.Checksum,
                CoverImagePath = ResolveCoverImageUrl(audio),
                TrackNumber = audio.TrackNumber,
                Date = audio.Date,
                Year = audio.Year,
                Rating = audio.Rating,
                PlayCount = audio.PlayCount,
                LastPlayed = audio.LastPlayed,
                Organized = audio.Organized,
                StudioId = audio.StudioId,
                CreatedAt = audio.CreatedAt,
                UpdatedAt = audio.UpdatedAt,
                TagIds = tagIds,
                PerformerIds = performerIds,
                GroupIds = groupIds,
            });
        });

        group.MapGet("/{id:int}/cover", async (int id, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null || string.IsNullOrWhiteSpace(audio.CoverImagePath))
                return Results.NotFound();

            if (IsDirectImageUrl(audio.CoverImagePath))
                return Results.Redirect(audio.CoverImagePath);

            var coverFilePath = ResolveLocalPath(audio.CoverImagePath);
            if (!System.IO.File.Exists(coverFilePath))
                return Results.NotFound();

            return Results.File(coverFilePath, GetImageContentType(coverFilePath));
        });

        group.MapPost("/{id:int}/cover", async (int id, IFormFile file, HttpContext ctx) =>
        {
            if (file.Length == 0)
                return Results.BadRequest(new { error = "Cover image file is empty." });

            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null)
                return Results.NotFound();

            var coverDirectory = GetCoverArtDirectory(ctx.RequestServices);
            Directory.CreateDirectory(coverDirectory);

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            var coverPath = Path.Combine(coverDirectory, $"audio-{id}-{DateTime.UtcNow.Ticks}{extension.ToLowerInvariant()}");
            await using (var stream = System.IO.File.Create(coverPath))
            {
                await file.CopyToAsync(stream);
            }

            audio.CoverImagePath = coverPath;
            audio.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { coverImagePath = ResolveCoverImageUrl(audio) });
        })
        .DisableAntiforgery();

        group.MapDelete("/{id:int}/cover", async (int id, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null)
                return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(audio.CoverImagePath) && !IsDirectImageUrl(audio.CoverImagePath))
            {
                var coverFilePath = ResolveLocalPath(audio.CoverImagePath);
                if (System.IO.File.Exists(coverFilePath))
                    System.IO.File.Delete(coverFilePath);
            }

            audio.CoverImagePath = null;
            audio.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        // ── Update ──────────────────────────────────────────────────
        group.MapPut("/{id:int}", async (int id, AudioUpdateDto update, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null) return Results.NotFound();

            if (update.Title != null) audio.Title = update.Title;
            if (update.Genre != null) audio.Genre = update.Genre;
            if (update.Date != null) audio.Date = update.Date;
            if (update.Rating.HasValue) audio.Rating = update.Rating.Value;
            if (update.TrackNumber.HasValue) audio.TrackNumber = update.TrackNumber.Value;
            if (update.Organized.HasValue) audio.Organized = update.Organized.Value;
            if (update.StudioId.HasValue) audio.StudioId = update.StudioId.Value == 0 ? null : update.StudioId.Value;
            if (update.CoverImagePath != null) audio.CoverImagePath = update.CoverImagePath;
            audio.UpdatedAt = DateTime.UtcNow;

            if (update.TagIds != null)
            {
                var existing = db.Set<AudioTagLink>().Where(t => t.AudioId == id);
                db.Set<AudioTagLink>().RemoveRange(existing);
                foreach (var tid in update.TagIds)
                    db.Set<AudioTagLink>().Add(new AudioTagLink { AudioId = id, TagId = tid });
            }
            if (update.PerformerIds != null)
            {
                var existing = db.Set<AudioPerformerLink>().Where(p => p.AudioId == id);
                db.Set<AudioPerformerLink>().RemoveRange(existing);
                foreach (var pid in update.PerformerIds)
                    db.Set<AudioPerformerLink>().Add(new AudioPerformerLink { AudioId = id, PerformerId = pid });
            }
            if (update.GroupIds != null)
            {
                var existing = db.Set<AudioGroupLink>().Where(g => g.AudioId == id);
                db.Set<AudioGroupLink>().RemoveRange(existing);
                foreach (var gid in update.GroupIds)
                    db.Set<AudioGroupLink>().Add(new AudioGroupLink { AudioId = id, GroupId = gid });
            }

            await db.SaveChangesAsync();
            return Results.Ok(audio);
        });

        // ── Delete ──────────────────────────────────────────────────
        group.MapDelete("/{id:int}", async (int id, HttpContext ctx, bool deleteFile = false) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null) return Results.NotFound();
            if (deleteFile && System.IO.File.Exists(audio.Path))
                System.IO.File.Delete(audio.Path);
            db.Set<AudioEntity>().Remove(audio);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ── Bulk delete ─────────────────────────────────────────────
        group.MapPost("/bulk-delete", async (BulkIdsRequest req, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audios = await db.Set<AudioEntity>()
                .Where(a => req.Ids.Contains(a.Id)).ToListAsync();
            db.Set<AudioEntity>().RemoveRange(audios);
            await db.SaveChangesAsync();
            return Results.Ok(new { deleted = audios.Count });
        });

        // ── Bulk update ─────────────────────────────────────────────
        group.MapPost("/bulk-update", async (BulkUpdateRequest req, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audios = await db.Set<AudioEntity>()
                .Where(a => req.Ids.Contains(a.Id)).ToListAsync();
            foreach (var audio in audios)
            {
                if (req.Rating.HasValue) audio.Rating = req.Rating.Value;
                if (req.Organized.HasValue) audio.Organized = req.Organized.Value;
                if (req.StudioId.HasValue) audio.StudioId = req.StudioId.Value == 0 ? null : req.StudioId.Value;
                audio.UpdatedAt = DateTime.UtcNow;

                if (req.TagIds != null)
                {
                    var existing = await db.Set<AudioTagLink>()
                        .Where(t => t.AudioId == audio.Id)
                        .ToListAsync();
                    ApplyBulkLinkUpdate(
                        db.Set<AudioTagLink>(),
                        existing,
                        req.TagIds,
                        req.TagIdsMode,
                        tid => new AudioTagLink { AudioId = audio.Id, TagId = tid },
                        link => link.TagId);
                }
                if (req.PerformerIds != null)
                {
                    var existing = await db.Set<AudioPerformerLink>()
                        .Where(p => p.AudioId == audio.Id)
                        .ToListAsync();
                    ApplyBulkLinkUpdate(
                        db.Set<AudioPerformerLink>(),
                        existing,
                        req.PerformerIds,
                        req.PerformerIdsMode,
                        pid => new AudioPerformerLink { AudioId = audio.Id, PerformerId = pid },
                        link => link.PerformerId);
                }
                if (req.GroupIds != null)
                {
                    var existing = await db.Set<AudioGroupLink>()
                        .Where(g => g.AudioId == audio.Id)
                        .ToListAsync();
                    ApplyBulkLinkUpdate(
                        db.Set<AudioGroupLink>(),
                        existing,
                        req.GroupIds,
                        req.GroupIdsMode,
                        gid => new AudioGroupLink { AudioId = audio.Id, GroupId = gid },
                        link => link.GroupId);
                }
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { updated = audios.Count });
        });

        // ── Stream audio file ───────────────────────────────────────
        group.MapGet("/{id:int}/stream", async (int id, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null) return Results.NotFound();
            if (!System.IO.File.Exists(audio.Path)) return Results.NotFound("Audio file not found on disk.");

            var ext = Path.GetExtension(audio.Path).ToLowerInvariant();
            var contentType = ext switch
            {
                ".mp3" => "audio/mpeg",
                ".flac" => "audio/flac",
                ".ogg" => "audio/ogg",
                ".wav" => "audio/wav",
                ".aac" => "audio/aac",
                ".m4a" => "audio/mp4",
                ".wma" => "audio/x-ms-wma",
                ".opus" => "audio/opus",
                ".aiff" or ".aif" => "audio/aiff",
                _ => "application/octet-stream",
            };

            return Results.File(audio.Path, contentType, enableRangeProcessing: true);
        });

        // ── Track play ──────────────────────────────────────────────
        group.MapPost("/{id:int}/play", async (int id, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var audio = await db.Set<AudioEntity>().FindAsync(id);
            if (audio == null) return Results.NotFound();

            audio.PlayCount++;
            audio.LastPlayed = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { audio.PlayCount, audio.LastPlayed });
        });

        // ── Genres list ─────────────────────────────────────────────
        group.MapGet("/genres", async (HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var genres = await db.Set<AudioEntity>()
                .Where(a => a.Genre != null)
                .Select(a => a.Genre!)
                .Distinct()
                .OrderBy(g => g)
                .ToListAsync();
            return Results.Ok(genres);
        });

        // ── Stats ───────────────────────────────────────────────────
        group.MapGet("/stats", async (HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var totalCount = await db.Set<AudioEntity>().CountAsync();
            var totalDuration = await db.Set<AudioEntity>().SumAsync(a => a.Duration ?? 0);
            var totalSize = await db.Set<AudioEntity>().SumAsync(a => a.FileSize ?? 0L);
            return Results.Ok(new { totalCount, totalDuration, totalSize });
        });

        // ── Cross-entity counts ─────────────────────────────────────
        group.MapGet("/count-by-performer/{performerId:int}", async (int performerId, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var count = await db.Set<AudioPerformerLink>()
                .Where(p => p.PerformerId == performerId).CountAsync();
            return Results.Ok(new { count });
        });

        group.MapGet("/count-by-group/{groupId:int}", async (int groupId, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var count = await db.Set<AudioGroupLink>()
                .Where(g => g.GroupId == groupId).CountAsync();
            return Results.Ok(new { count });
        });

        group.MapGet("/count-by-tag/{tagId:int}", async (int tagId, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var count = await db.Set<AudioTagLink>()
                .Where(t => t.TagId == tagId).CountAsync();
            return Results.Ok(new { count });
        });

        group.MapGet("/count-by-studio/{studioId:int}", async (int studioId, HttpContext ctx) =>
        {
            var db = ctx.RequestServices.GetRequiredService<DbContext>();
            var count = await db.Set<AudioEntity>()
                .Where(a => a.StudioId == studioId).CountAsync();
            return Results.Ok(new { count });
        });

        // ── Settings ────────────────────────────────────────────────
        group.MapGet("/settings", async () =>
        {
            var scanPaths = await Store.GetAsync("scan_paths") ?? "[]";
            var audioExts = await Store.GetAsync("audio_extensions")
                ?? @"["".mp3"","".flac"","".ogg"","".wav"","".m4a"","".aac"","".wma"","".opus""]";
            var extractCover = await Store.GetAsync("extract_cover_art") ?? "true";
            return Results.Ok(new
            {
                scanPaths = JsonSerializer.Deserialize<string[]>(scanPaths),
                audioExtensions = JsonSerializer.Deserialize<string[]>(audioExts),
                extractCoverArt = bool.Parse(extractCover),
            });
        });

        group.MapPut("/settings", async (AudioSettingsDto settings) =>
        {
            await Store.SetAsync("scan_paths", JsonSerializer.Serialize(settings.ScanPaths ?? []));
            if (settings.AudioExtensions is { Count: > 0 })
                await Store.SetAsync("audio_extensions", JsonSerializer.Serialize(settings.AudioExtensions));
            await Store.SetAsync("extract_cover_art", settings.ExtractCoverArt.ToString().ToLowerInvariant());
            if (settings.ScanPaths is { Count: > 0 })
                await Store.SetAsync("scan_path", settings.ScanPaths[0]);
            return Results.Ok();
        });

        // ── Trigger scan ────────────────────────────────────────────
        group.MapPost("/scan", async (HttpContext ctx) =>
        {
            var client = new HttpClient { BaseAddress = new Uri($"{ctx.Request.Scheme}://{ctx.Request.Host}") };
            var response = await client.PostAsync("/api/extensions/cove.official.audios/jobs/scan-audios", null);
            return response.IsSuccessStatusCode ? Results.Accepted() : Results.StatusCode(500);
        });
    }

    // ── UI ──────────────────────────────────────────────────────────

    public override UIManifest GetUIManifest()
    {
        return ManifestBuilder()
            .WithRuntimeVersion("v1")
            .AddPage(
                route: "audios",
                label: "Audios",
                componentName: "AudiosPage",
                icon: "music",
                detailRoute: "audio",
                showInNav: true,
                navOrder: 35)
            .AddPage(
                route: "audio",
                label: "Audio Detail",
                componentName: "AudioDetailPage",
                showInNav: false)
            .AddTab("performer", "audios", "Audios", "AudiosPerformerTab",
                order: 35, countEndpoint: "/api/ext/audios/count-by-performer/{entityId}", icon: "music")
            .AddTab("group", "audios", "Audios", "AudiosGroupTab",
                order: 25, countEndpoint: "/api/ext/audios/count-by-group/{entityId}", icon: "music")
            .AddTab("tag", "audios", "Audios", "AudiosTagTab",
                order: 65, countEndpoint: "/api/ext/audios/count-by-tag/{entityId}", icon: "music")
            .AddTab("studio", "audios", "Audios", "AudiosStudioTab",
                order: 35, countEndpoint: "/api/ext/audios/count-by-studio/{entityId}", icon: "music")
            .AddSlot("tag-card-footer", "TagAudiosCardFooter", id: "tag-audios-card-footer", order: 65)
            .AddSlot("studio-card-footer", "StudioAudiosCardFooter", id: "studio-audios-card-footer", order: 65)
            .AddSlot("group-card-footer", "GroupAudiosCardFooter", id: "group-audios-card-footer", order: 65)
            .AddSettingsSection("library", "Audio Extensions", "AudioExtensionsSettings", order: 50, targetSection: "extensions")
            .Build();
    }

    // ── Jobs ────────────────────────────────────────────────────────


    protected override void DefineJobs()
    {
        Job("scan-audios", "Scan Audio Files", ScanAudioFilesAsync,
            description: "Scan configured library paths for audio files and add them to the database.",
            supportsParameters: true);
    }

    // -- Core scan participation ----------------------------------------

    public override async Task ScanAsync(ScanContext context, CancellationToken ct = default)
    {
        var scanPaths = context.Paths
            .Where(p => !p.ExcludeAudio && !p.IsFile)
            .Select(p => p.Path)
            .ToArray();

        if (scanPaths.Length == 0) return;

        await ScanAudioFilesCore(scanPaths, context.Progress, context.Services, context.Rescan, ct);
    }

    private async Task ScanAudioFilesAsync(
        IReadOnlyDictionary<string, string>? parameters,
        Cove.Plugins.IJobProgress progress,
        CancellationToken ct)
    {
        var scanPathsJson = await Store.GetAsync("scan_paths", ct) ?? "[]";
        var scanPaths = JsonSerializer.Deserialize<string[]>(scanPathsJson) ?? [];

        var paramPath = parameters?.GetValueOrDefault("path");
        if (!string.IsNullOrWhiteSpace(paramPath))
            scanPaths = [paramPath];

        if (scanPaths.Length == 0)
        {
            var legacy = await Store.GetAsync("scan_path", ct);
            if (!string.IsNullOrWhiteSpace(legacy)) scanPaths = [legacy];
        }

        if (scanPaths.Length == 0 && _services is not null)
        {
            using var configScope = _services.CreateScope();
            var config = configScope.ServiceProvider.GetRequiredService<CoveConfiguration>();
            scanPaths = config.CovePaths
                .Where(p => !p.ExcludeAudio && !string.IsNullOrWhiteSpace(p.Path))
                .Select(p => p.Path)
                .ToArray();
        }

        var validPaths = scanPaths.Where(Directory.Exists).ToArray();
        if (validPaths.Length == 0)
        {
            progress.Report(100, "No valid scan paths configured. Set them in extension settings.");
            return;
        }

        if (_services is null)
        {
            progress.Report(100, "Extension not initialized - service provider unavailable.");
            return;
        }

        await ScanAudioFilesCore(validPaths, progress, _services, false, ct);
    }

    private async Task ScanAudioFilesCore(
        string[] scanPaths,
        Cove.Plugins.IJobProgress progress,
        IServiceProvider services,
        bool rescan,
        CancellationToken ct)
    {
        var audioExtensionsJson = await Store.GetAsync("audio_extensions", ct)
            ?? @"["".mp3"","".flac"","".ogg"","".wav"","".m4a"","".aac"","".wma"","".opus""]";
        var audioExtensions = new HashSet<string>(
            JsonSerializer.Deserialize<string[]>(audioExtensionsJson) ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (audioExtensions.Count == 0)
            audioExtensions = [".mp3", ".flac", ".ogg", ".wav", ".aac", ".m4a", ".wma", ".opus", ".aiff", ".aif"];
        var extractCoverArt = bool.TryParse(await Store.GetAsync("extract_cover_art", ct), out var extractCover) ? extractCover : true;

        var validPaths = scanPaths.Where(Directory.Exists).ToArray();

        var allFiles = validPaths
            .SelectMany(p => Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress.Report(0, $"Found {allFiles.Count} audio files across {validPaths.Length} path(s).");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IOptions<CoveConfiguration>>().Value;
        var audioSet = db.Set<AudioEntity>();

        var existingAudios = await audioSet.ToListAsync(ct);
        var existingByPath = existingAudios.ToDictionary(a => a.Path, StringComparer.OrdinalIgnoreCase);
        var coverDirectory = GetCoverArtDirectory(config);
        Directory.CreateDirectory(coverDirectory);

        var processed = 0;
        var added = 0;
        var updated = 0;

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            existingByPath.TryGetValue(file, out var audio);
            var isNew = audio == null;
            if (isNew)
            {
                audio = new AudioEntity
                {
                    Title = System.IO.Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                audioSet.Add(audio);
                existingByPath[file] = audio;
                added++;
            }

            var info = new FileInfo(file);
            audio!.FileSize = info.Length;

            var needsMetadata = isNew
                || rescan
                || string.IsNullOrWhiteSpace(audio.Artist)
                || string.IsNullOrWhiteSpace(audio.Album)
                || string.IsNullOrWhiteSpace(audio.Genre)
                || !audio.Duration.HasValue
                || !audio.Bitrate.HasValue
                || !audio.SampleRate.HasValue
                || !audio.Channels.HasValue
                || (extractCoverArt && string.IsNullOrWhiteSpace(audio.CoverImagePath));

            if (needsMetadata)
            {
                ApplyAudioMetadata(audio, file, coverDirectory, extractCoverArt);
                audio.UpdatedAt = DateTime.UtcNow;
                if (!isNew) updated++;
            }

            if (processed % 50 == 0 || processed == allFiles.Count)
            {
                var pct = (double)processed / allFiles.Count * 100;
                progress.Report(pct, $"Processed {processed}/{allFiles.Count} files ({added} new, {updated} updated)");
            }
        }

        if (added > 0 || updated > 0)
            await db.SaveChangesAsync(ct);

        await Store.SetAsync("last_scan", DateTime.UtcNow.ToString("o"), ct);
        await Store.SetAsync("last_scan_count", allFiles.Count.ToString(), ct);

        progress.Report(100, $"Scan complete. {validPaths.Length} path(s), {processed} files, {added} new entries, {updated} updated.");
    }

    // -- Auto-tag participation -----------------------------------------

    public override async Task AutoTagAsync(AutoTagContext context, CancellationToken ct = default)
    {
        using var scope = context.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();

        var audios = await db.Set<AudioEntity>().ToListAsync(ct);
        var existingPerformerLinks = await db.Set<AudioPerformerLink>().ToListAsync(ct);
        var existingTagLinks = await db.Set<AudioTagLink>().ToListAsync(ct);

        var performerLinkSet = new HashSet<(int, int)>(existingPerformerLinks.Select(l => (l.AudioId, l.PerformerId)));
        var tagLinkSet = new HashSet<(int, int)>(existingTagLinks.Select(l => (l.AudioId, l.TagId)));

        int matched = 0;
        for (int i = 0; i < audios.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var audio = audios[i];
            var filePath = audio.Path.ToLowerInvariant();
            var basename = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            bool changed = false;

            foreach (var p in context.Performers)
            {
                if (performerLinkSet.Contains((audio.Id, p.Id))) continue;
                var names = new List<string> { p.Name.ToLowerInvariant() };
                names.AddRange(p.Aliases.Select(a => a.ToLowerInvariant()));

                if (names.Any(n => n.Length > 2 && (filePath.Contains(n) || basename.Contains(n))))
                {
                    db.Set<AudioPerformerLink>().Add(new AudioPerformerLink { AudioId = audio.Id, PerformerId = p.Id });
                    performerLinkSet.Add((audio.Id, p.Id));
                    changed = true;
                }
            }

            foreach (var s in context.Studios)
            {
                if (audio.StudioId.HasValue) continue;
                var name = s.Name.ToLowerInvariant();
                if (name.Length > 2 && (filePath.Contains(name) || basename.Contains(name)))
                {
                    audio.StudioId = s.Id;
                    changed = true;
                    break;
                }
            }

            foreach (var t in context.Tags)
            {
                if (tagLinkSet.Contains((audio.Id, t.Id))) continue;
                var names = new List<string> { t.Name.ToLowerInvariant() };
                names.AddRange(t.Aliases.Select(a => a.ToLowerInvariant()));

                if (names.Any(n => n.Length > 2 && (filePath.Contains(n) || basename.Contains(n))))
                {
                    db.Set<AudioTagLink>().Add(new AudioTagLink { AudioId = audio.Id, TagId = t.Id });
                    tagLinkSet.Add((audio.Id, t.Id));
                    changed = true;
                }
            }

            if (changed) matched++;
            if ((i + 1) % 50 == 0 || i == audios.Count - 1)
                context.Progress.Report((double)(i + 1) / audios.Count * 100, $"Auto-tagged {i + 1}/{audios.Count} audios ({matched} matched)");
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Event handlers ──────────────────────────────────────────────

    protected override void DefineEventHandlers()
    {
        OnDeleted("tag", async (evt, ct) =>
        {
            if (_services == null) return;
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            var links = await db.Set<AudioTagLink>()
                .Where(t => t.TagId == evt.EntityId).ToListAsync(ct);
            db.Set<AudioTagLink>().RemoveRange(links);
            await db.SaveChangesAsync(ct);
        });

        OnDeleted("performer", async (evt, ct) =>
        {
            if (_services == null) return;
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            var links = await db.Set<AudioPerformerLink>()
                .Where(p => p.PerformerId == evt.EntityId).ToListAsync(ct);
            db.Set<AudioPerformerLink>().RemoveRange(links);
            await db.SaveChangesAsync(ct);
        });

        OnDeleted("group", async (evt, ct) =>
        {
            if (_services == null) return;
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            var links = await db.Set<AudioGroupLink>()
                .Where(g => g.GroupId == evt.EntityId).ToListAsync(ct);
            db.Set<AudioGroupLink>().RemoveRange(links);
            await db.SaveChangesAsync(ct);
        });

        OnDeleted("studio", async (evt, ct) =>
        {
            if (_services == null) return;
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            var audios = await db.Set<AudioEntity>()
                .Where(a => a.StudioId == evt.EntityId).ToListAsync(ct);
            foreach (var audio in audios) audio.StudioId = null;
            await db.SaveChangesAsync(ct);
        });
    }

    private static IQueryable<AudioEntity> ApplyAdvancedFilters(IQueryable<AudioEntity> query, DbContext db, string filtersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(filtersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return query;

            var root = document.RootElement;

            if (root.TryGetProperty("genre", out var genreCriterion))
                query = ApplyGenreCriterion(query, genreCriterion);

            if (root.TryGetProperty("organized", out var organizedCriterion))
                query = ApplyOrganizedCriterion(query, organizedCriterion);

            if (root.TryGetProperty("rating", out var ratingCriterion))
                query = ApplyRatingCriterion(query, ratingCriterion);

            if (root.TryGetProperty("studios", out var studioCriterion))
                query = ApplyStudioCriterion(query, studioCriterion);

            if (root.TryGetProperty("tags", out var tagCriterion))
                query = ApplyTagCriterion(query, db, tagCriterion);

            if (root.TryGetProperty("performers", out var performerCriterion))
                query = ApplyPerformerCriterion(query, db, performerCriterion);

            if (root.TryGetProperty("groups", out var groupCriterion))
                query = ApplyGroupCriterion(query, db, groupCriterion);
        }
        catch
        {
            // Ignore invalid filter payloads and fall back to the basic query.
        }

        return query;
    }

    private static IQueryable<AudioEntity> ApplyGenreCriterion(IQueryable<AudioEntity> query, JsonElement criterion)
    {
        var modifier = GetCriterionModifier(criterion) ?? "INCLUDES";
        if (modifier == "IS_NULL")
            return query.Where(a => a.Genre == null || a.Genre == "");
        if (modifier == "NOT_NULL")
            return query.Where(a => a.Genre != null && a.Genre != "");

        var value = GetStringValue(criterion);
        if (string.IsNullOrWhiteSpace(value))
            return query;

        var search = value.ToLower();
        return modifier switch
        {
            "EQUALS" => query.Where(a => a.Genre != null && a.Genre.ToLower() == search),
            "NOT_EQUALS" => query.Where(a => a.Genre == null || a.Genre.ToLower() != search),
            "EXCLUDES" => query.Where(a => a.Genre == null || !a.Genre.ToLower().Contains(search)),
            _ => query.Where(a => a.Genre != null && a.Genre.ToLower().Contains(search)),
        };
    }

    private static IQueryable<AudioEntity> ApplyOrganizedCriterion(IQueryable<AudioEntity> query, JsonElement criterion)
    {
        var value = GetBooleanValue(criterion);
        return value.HasValue ? query.Where(a => a.Organized == value.Value) : query;
    }

    private static IQueryable<AudioEntity> ApplyRatingCriterion(IQueryable<AudioEntity> query, JsonElement criterion)
    {
        var modifier = GetCriterionModifier(criterion) ?? "EQUALS";
        if (modifier == "IS_NULL")
            return query.Where(_ => false);
        if (modifier == "NOT_NULL")
            return query;

        var value = GetIntValue(criterion, "value");
        var value2 = GetIntValue(criterion, "value2");
        if (!value.HasValue)
            return query;

        return modifier switch
        {
            "NOT_EQUALS" => query.Where(a => a.Rating != value.Value),
            "GREATER_THAN" => query.Where(a => a.Rating > value.Value),
            "LESS_THAN" => query.Where(a => a.Rating < value.Value),
            "BETWEEN" when value2.HasValue => query.Where(a => a.Rating >= value.Value && a.Rating <= value2.Value),
            "NOT_BETWEEN" when value2.HasValue => query.Where(a => a.Rating < value.Value || a.Rating > value2.Value),
            _ => query.Where(a => a.Rating == value.Value),
        };
    }

    private static IQueryable<AudioEntity> ApplyStudioCriterion(IQueryable<AudioEntity> query, JsonElement criterion)
    {
        var modifier = GetCriterionModifier(criterion) ?? "INCLUDES";
        if (modifier == "IS_NULL")
            return query.Where(a => a.StudioId == null);
        if (modifier == "NOT_NULL")
            return query.Where(a => a.StudioId != null);

        var includeIds = GetIntList(criterion, "value");
        var excludeIds = GetIntList(criterion, "excludes");
        if (modifier is "EXCLUDES" or "EXCLUDES_ALL")
        {
            excludeIds = excludeIds.Concat(includeIds).Distinct().ToList();
            includeIds.Clear();
        }

        if (includeIds.Count > 0)
            query = query.Where(a => a.StudioId.HasValue && includeIds.Contains(a.StudioId.Value));

        if (excludeIds.Count > 0)
            query = query.Where(a => !a.StudioId.HasValue || !excludeIds.Contains(a.StudioId.Value));

        return query;
    }

    private static IQueryable<AudioEntity> ApplyTagCriterion(IQueryable<AudioEntity> query, DbContext db, JsonElement criterion)
    {
        var modifier = GetCriterionModifier(criterion) ?? "INCLUDES";
        var includeIds = GetIntList(criterion, "value");
        var excludeIds = GetIntList(criterion, "excludes");

        if (modifier is "EXCLUDES" or "EXCLUDES_ALL")
        {
            excludeIds = excludeIds.Concat(includeIds).Distinct().ToList();
            includeIds.Clear();
        }

        if (includeIds.Count > 0)
        {
            if (modifier == "INCLUDES_ALL")
            {
                foreach (var tagId in includeIds)
                {
                    var audioIds = db.Set<AudioTagLink>().Where(t => t.TagId == tagId).Select(t => t.AudioId);
                    query = query.Where(a => audioIds.Contains(a.Id));
                }
            }
            else
            {
                var audioIds = db.Set<AudioTagLink>().Where(t => includeIds.Contains(t.TagId)).Select(t => t.AudioId);
                query = query.Where(a => audioIds.Contains(a.Id));
            }
        }

        if (excludeIds.Count > 0)
        {
            if (modifier == "EXCLUDES_ALL")
            {
                var audioIds = db.Set<AudioTagLink>()
                    .Where(t => excludeIds.Contains(t.TagId))
                    .GroupBy(t => t.AudioId)
                    .Where(group => group.Select(item => item.TagId).Distinct().Count() == excludeIds.Count)
                    .Select(group => group.Key);
                query = query.Where(a => !audioIds.Contains(a.Id));
            }
            else
            {
                var audioIds = db.Set<AudioTagLink>().Where(t => excludeIds.Contains(t.TagId)).Select(t => t.AudioId);
                query = query.Where(a => !audioIds.Contains(a.Id));
            }
        }

        return query;
    }

    private static IQueryable<AudioEntity> ApplyPerformerCriterion(IQueryable<AudioEntity> query, DbContext db, JsonElement criterion)
    {
        var modifier = GetCriterionModifier(criterion) ?? "INCLUDES";
        var includeIds = GetIntList(criterion, "value");
        var excludeIds = GetIntList(criterion, "excludes");

        if (modifier is "EXCLUDES" or "EXCLUDES_ALL")
        {
            excludeIds = excludeIds.Concat(includeIds).Distinct().ToList();
            includeIds.Clear();
        }

        if (includeIds.Count > 0)
        {
            if (modifier == "INCLUDES_ALL")
            {
                foreach (var performerId in includeIds)
                {
                    var audioIds = db.Set<AudioPerformerLink>().Where(p => p.PerformerId == performerId).Select(p => p.AudioId);
                    query = query.Where(a => audioIds.Contains(a.Id));
                }
            }
            else
            {
                var audioIds = db.Set<AudioPerformerLink>().Where(p => includeIds.Contains(p.PerformerId)).Select(p => p.AudioId);
                query = query.Where(a => audioIds.Contains(a.Id));
            }
        }

        if (excludeIds.Count > 0)
        {
            if (modifier == "EXCLUDES_ALL")
            {
                var audioIds = db.Set<AudioPerformerLink>()
                    .Where(p => excludeIds.Contains(p.PerformerId))
                    .GroupBy(p => p.AudioId)
                    .Where(group => group.Select(item => item.PerformerId).Distinct().Count() == excludeIds.Count)
                    .Select(group => group.Key);
                query = query.Where(a => !audioIds.Contains(a.Id));
            }
            else
            {
                var audioIds = db.Set<AudioPerformerLink>().Where(p => excludeIds.Contains(p.PerformerId)).Select(p => p.AudioId);
                query = query.Where(a => !audioIds.Contains(a.Id));
            }
        }

        return query;
    }

    private static IQueryable<AudioEntity> ApplyGroupCriterion(IQueryable<AudioEntity> query, DbContext db, JsonElement criterion)
    {
        var modifier = GetCriterionModifier(criterion) ?? "INCLUDES";
        var includeIds = GetIntList(criterion, "value");
        var excludeIds = GetIntList(criterion, "excludes");

        if (modifier is "EXCLUDES" or "EXCLUDES_ALL")
        {
            excludeIds = excludeIds.Concat(includeIds).Distinct().ToList();
            includeIds.Clear();
        }

        if (includeIds.Count > 0)
        {
            if (modifier == "INCLUDES_ALL")
            {
                foreach (var groupId in includeIds)
                {
                    var audioIds = db.Set<AudioGroupLink>().Where(g => g.GroupId == groupId).Select(g => g.AudioId);
                    query = query.Where(a => audioIds.Contains(a.Id));
                }
            }
            else
            {
                var audioIds = db.Set<AudioGroupLink>().Where(g => includeIds.Contains(g.GroupId)).Select(g => g.AudioId);
                query = query.Where(a => audioIds.Contains(a.Id));
            }
        }

        if (excludeIds.Count > 0)
        {
            if (modifier == "EXCLUDES_ALL")
            {
                var audioIds = db.Set<AudioGroupLink>()
                    .Where(g => excludeIds.Contains(g.GroupId))
                    .GroupBy(g => g.AudioId)
                    .Where(group => group.Select(item => item.GroupId).Distinct().Count() == excludeIds.Count)
                    .Select(group => group.Key);
                query = query.Where(a => !audioIds.Contains(a.Id));
            }
            else
            {
                var audioIds = db.Set<AudioGroupLink>().Where(g => excludeIds.Contains(g.GroupId)).Select(g => g.AudioId);
                query = query.Where(a => !audioIds.Contains(a.Id));
            }
        }

        return query;
    }

    private static void ApplyBulkLinkUpdate<TLink>(
        DbSet<TLink> set,
        IReadOnlyCollection<TLink> existingLinks,
        IReadOnlyCollection<int>? requestedIds,
        string? mode,
        Func<int, TLink> createLink,
        Func<TLink, int> getRelatedId)
        where TLink : class
    {
        if (requestedIds == null)
            return;

        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "SET" : mode.Trim().ToUpperInvariant();
        var ids = requestedIds.Distinct().ToHashSet();
        var existingIds = existingLinks.Select(getRelatedId).ToHashSet();

        switch (normalizedMode)
        {
            case "ADD":
                foreach (var id in ids.Except(existingIds))
                    set.Add(createLink(id));
                break;
            case "REMOVE":
                set.RemoveRange(existingLinks.Where(link => ids.Contains(getRelatedId(link))));
                break;
            default:
                set.RemoveRange(existingLinks.Where(link => !ids.Contains(getRelatedId(link))));
                foreach (var id in ids.Except(existingIds))
                    set.Add(createLink(id));
                break;
        }
    }

    private static string? GetCriterionModifier(JsonElement criterion)
    {
        return criterion.TryGetProperty("modifier", out var modifierElement) && modifierElement.ValueKind == JsonValueKind.String
            ? modifierElement.GetString()?.ToUpperInvariant()
            : null;
    }

    private static string? GetStringValue(JsonElement criterion)
    {
        if (!criterion.TryGetProperty("value", out var valueElement))
            return null;

        return valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() : valueElement.ToString();
    }

    private static bool? GetBooleanValue(JsonElement criterion)
    {
        if (!criterion.TryGetProperty("value", out var valueElement))
            return null;

        return valueElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(valueElement.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static int? GetIntValue(JsonElement criterion, string propertyName)
    {
        if (!criterion.TryGetProperty(propertyName, out var valueElement))
            return null;

        return valueElement.ValueKind switch
        {
            JsonValueKind.Number when valueElement.TryGetInt32(out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(valueElement.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static List<int> GetIntList(JsonElement criterion, string propertyName)
    {
        if (!criterion.TryGetProperty(propertyName, out var valueElement) || valueElement.ValueKind != JsonValueKind.Array)
            return [];

        var values = new List<int>();
        foreach (var item in valueElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var numericValue))
            {
                values.Add(numericValue);
                continue;
            }

            if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var stringValue))
            {
                values.Add(stringValue);
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var objectNumericValue))
                    values.Add(objectNumericValue);
                else if (idElement.ValueKind == JsonValueKind.String && int.TryParse(idElement.GetString(), out var objectStringValue))
                    values.Add(objectStringValue);
            }
        }

        return values.Distinct().ToList();
    }

    private static string? ResolveCoverImageUrl(AudioEntity audio)
    {
        if (string.IsNullOrWhiteSpace(audio.CoverImagePath))
            return null;

        if (IsDirectImageUrl(audio.CoverImagePath))
            return audio.CoverImagePath;

        return $"/api/ext/audios/{audio.Id}/cover?v={Uri.EscapeDataString(audio.UpdatedAt.ToString("O"))}";
    }

    private static bool IsDirectImageUrl(string path)
        => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith('/');

    private static string ResolveLocalPath(string path)
        => Path.IsPathRooted(path) ? path : Path.GetFullPath(path);

    private static string GetCoverArtDirectory(IServiceProvider services)
        => GetCoverArtDirectory(services.GetRequiredService<IOptions<CoveConfiguration>>().Value);

    private static string GetCoverArtDirectory(CoveConfiguration config)
        => ResolveLocalPath(Path.Combine(config.GeneratedPath, "extensions", "audios", "covers"));

    private static string GetImageContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/jpeg",
        };

    private static void ApplyAudioMetadata(AudioEntity audio, string filePath, string coverDirectory, bool extractCoverArt)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;
            var properties = tagFile.Properties;

            audio.Title = !string.IsNullOrWhiteSpace(tag.Title)
                ? tag.Title
                : Path.GetFileNameWithoutExtension(filePath);
            audio.Artist = tag.Performers?.FirstOrDefault();
            audio.Album = tag.Album;
            audio.Genre = tag.Genres?.FirstOrDefault();
            audio.Duration = properties.Duration.TotalSeconds > 0 ? properties.Duration.TotalSeconds : audio.Duration;
            audio.Bitrate = properties.AudioBitrate > 0 ? properties.AudioBitrate : audio.Bitrate;
            audio.SampleRate = properties.AudioSampleRate > 0 ? properties.AudioSampleRate : audio.SampleRate;
            audio.Channels = properties.AudioChannels > 0 ? properties.AudioChannels : audio.Channels;
            audio.TrackNumber = tag.Track > 0 ? (int)tag.Track : audio.TrackNumber;
            audio.Year = tag.Year > 0 ? (int)tag.Year : audio.Year;

            if (extractCoverArt && tag.Pictures is { Length: > 0 } && !HasUploadedCover(audio))
            {
                var picture = tag.Pictures[0];
                var extension = GetPictureExtension(picture);
                var coverPath = Path.Combine(coverDirectory, $"{ComputeStableHash(filePath)}{extension}");
                System.IO.File.WriteAllBytes(coverPath, picture.Data.Data);
                audio.CoverImagePath = coverPath;
            }
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(audio.Title))
                audio.Title = Path.GetFileNameWithoutExtension(filePath);
        }
    }

    private static string GetPictureExtension(IPicture picture)
        => picture.MimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".jpg",
        };

    private static string ComputeStableHash(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool HasUploadedCover(AudioEntity audio)
    {
        if (string.IsNullOrWhiteSpace(audio.CoverImagePath))
            return false;

        var fileName = Path.GetFileName(audio.CoverImagePath);
        return fileName.StartsWith($"audio-{audio.Id}-", StringComparison.OrdinalIgnoreCase);
    }
}

// ── Entities ─────────────────────────────────────────────────────

public class AudioEntity
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Path { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public string? Date { get; set; }
    public double? Duration { get; set; }
    public int? Bitrate { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public long? FileSize { get; set; }
    public string? Checksum { get; set; }
    public string? CoverImagePath { get; set; }
    public int? TrackNumber { get; set; }
    public int? Year { get; set; }
    public int? StudioId { get; set; }
    public int Rating { get; set; }
    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }
    public bool Organized { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AudioTagLink
{
    public int AudioId { get; set; }
    public int TagId { get; set; }
}

public class AudioPerformerLink
{
    public int AudioId { get; set; }
    public int PerformerId { get; set; }
}

public class AudioGroupLink
{
    public int AudioId { get; set; }
    public int GroupId { get; set; }
}

// ── DTOs ─────────────────────────────────────────────────────────

public class AudioSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Genre { get; set; }
    public double? Duration { get; set; }
    public string? CoverImagePath { get; set; }
    public int Rating { get; set; }
    public int PlayCount { get; set; }
    public string? Date { get; set; }
    public bool Organized { get; set; }
    public int? StudioId { get; set; }
    public int TagCount { get; set; }
    public int PerformerCount { get; set; }
    public int GroupCount { get; set; }
    public string? StudioName { get; set; }
    public List<EntityRefDto> Tags { get; set; } = [];
    public List<EntityRefDto> Performers { get; set; } = [];
    public List<EntityRefDto> Groups { get; set; } = [];
}

public class EntityRefDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? ImagePath { get; set; }
}

public class AudioDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public string? Date { get; set; }
    public double? Duration { get; set; }
    public int? Bitrate { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public long? FileSize { get; set; }
    public string? Checksum { get; set; }
    public string? CoverImagePath { get; set; }
    public int? TrackNumber { get; set; }
    public int? Year { get; set; }
    public int? StudioId { get; set; }
    public int Rating { get; set; }
    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }
    public bool Organized { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<int> TagIds { get; set; } = [];
    public List<int> PerformerIds { get; set; } = [];
    public List<int> GroupIds { get; set; } = [];
}

public class AudioUpdateDto
{
    public string? Title { get; set; }
    public string? Genre { get; set; }
    public string? Date { get; set; }
    public string? CoverImagePath { get; set; }
    public int? Rating { get; set; }
    public int? TrackNumber { get; set; }
    public bool? Organized { get; set; }
    public int? StudioId { get; set; }
    public List<int>? TagIds { get; set; }
    public List<int>? PerformerIds { get; set; }
    public List<int>? GroupIds { get; set; }
}

public class BulkIdsRequest
{
    public List<int> Ids { get; set; } = [];
}

public class BulkUpdateRequest
{
    public List<int> Ids { get; set; } = [];
    public int? Rating { get; set; }
    public bool? Organized { get; set; }
    public int? StudioId { get; set; }
    public List<int>? TagIds { get; set; }
    public string? TagIdsMode { get; set; }
    public List<int>? PerformerIds { get; set; }
    public string? PerformerIdsMode { get; set; }
    public List<int>? GroupIds { get; set; }
    public string? GroupIdsMode { get; set; }
}

public class AudioSettingsDto
{
    public List<string>? ScanPaths { get; set; }
    public List<string>? AudioExtensions { get; set; }
    public bool ExtractCoverArt { get; set; } = true;
}
