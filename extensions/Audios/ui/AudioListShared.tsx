import { Layers, Music, Play, Tag, User } from "lucide-react";
import {
  PopoverButton,
  RatingBanner,
  formatDuration,
  type FindFilter,
} from "@cove/runtime/components";
import { appendAudioFilters, type AudioObjectFilter } from "./AudioFilters";

export const AUDIO_API = "/api/ext/audios";

export interface AudioSummary {
  id: number;
  title: string;
  path: string;
  genre?: string;
  duration?: number;
  coverImagePath?: string;
  rating: number;
  playCount: number;
  date?: string;
  organized: boolean;
  studioId?: number;
  studioName?: string;
  tagCount: number;
  performerCount: number;
  groupCount: number;
  tags?: { id: number; name: string }[];
  performers?: { id: number; name: string; imagePath?: string }[];
  groups?: { id: number; name: string }[];
}

export interface AudioListResponse {
  items: AudioSummary[];
  totalCount: number;
}

export const AUDIO_SORT_OPTIONS = [
  { value: "title", label: "Title" },
  { value: "date", label: "Date" },
  { value: "duration", label: "Duration" },
  { value: "rating", label: "Rating" },
  { value: "playcount", label: "Play Count" },
  { value: "file_size", label: "File Size" },
  { value: "created_at", label: "Recently Added" },
  { value: "updated_at", label: "Recently Updated" },
  { value: "random", label: "Random" },
];

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (!response.ok) {
    throw new Error(`${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json();
}

export function buildAudioListUrl(
  filter: FindFilter,
  objectFilter: AudioObjectFilter = {},
  scopedParams: Record<string, string | number | undefined | null> = {},
) {
  const params = new URLSearchParams({
    page: String(filter.page ?? 1),
    perPage: String(filter.perPage ?? 40),
    sort: filter.sort ?? "title",
    direction: filter.direction ?? "asc",
  });

  if (filter.q) {
    params.set("q", filter.q);
  }

  appendAudioFilters(params, objectFilter);

  for (const [key, value] of Object.entries(scopedParams)) {
    if (value != null && value !== "") {
      params.set(key, String(value));
    }
  }

  return `${AUDIO_API}?${params.toString()}`;
}

export function fetchAudioList(
  filter: FindFilter,
  objectFilter: AudioObjectFilter = {},
  scopedParams: Record<string, string | number | undefined | null> = {},
) {
  return apiFetch<AudioListResponse>(buildAudioListUrl(filter, objectFilter, scopedParams));
}

export function AudioCard({
  audio,
  onClick,
  onNavigate,
  onPlay,
  playing = false,
  selecting = false,
  selected = false,
  onSelect,
}: {
  audio: AudioSummary;
  onClick: () => void;
  onNavigate: (route: any) => void;
  onPlay?: () => void;
  playing?: boolean;
  selecting?: boolean;
  selected?: boolean;
  onSelect: () => void;
}) {
  const performers = audio.performers ?? [];
  const tags = audio.tags ?? [];
  const groups = audio.groups ?? [];
  const hasPopovers = performers.length > 0 || tags.length > 0 || groups.length > 0 || audio.organized;

  return (
    <div
      className={`scene-card group flex h-full cursor-pointer flex-col overflow-hidden rounded border bg-card ${
        selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/50"
      } ${playing ? "ring-2 ring-accent/60" : ""}`}
      onClick={selecting ? onSelect : onClick}
    >
      <div className="scene-card-preview relative aspect-square overflow-hidden bg-black">
        {audio.coverImagePath ? (
          <img
            src={audio.coverImagePath}
            alt=""
            className="scene-card-preview-image h-full w-full object-cover"
            loading="lazy"
            onError={(event) => {
              (event.target as HTMLImageElement).style.display = "none";
            }}
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center bg-gradient-to-br from-surface to-card">
            <Music className="h-12 w-12 text-muted/30" />
          </div>
        )}

        <div className={`absolute left-1 top-1 z-10 ${selected || selecting ? "opacity-100" : "opacity-0 group-hover:opacity-100"} transition-opacity`}>
          <input
            type="checkbox"
            checked={selected}
            onChange={(event) => {
              event.stopPropagation();
              onSelect();
            }}
            onClick={(event) => event.stopPropagation()}
            className="h-4 w-4 cursor-pointer rounded border-border accent-accent"
          />
        </div>

        {audio.duration != null && audio.duration > 0 && (
          <div className="scene-specs-overlay absolute bottom-0 right-0 z-[5] flex items-center gap-0.5 px-1.5 py-1 text-xs text-white">
            <span className="rounded bg-black/70 px-1 py-0.5">{formatDuration(audio.duration)}</span>
          </div>
        )}

        {onPlay && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <button
              onClick={(event) => {
                event.stopPropagation();
                onPlay();
              }}
              className="pointer-events-auto flex h-10 w-10 items-center justify-center rounded-full bg-accent/90 opacity-0 shadow-lg transition-opacity group-hover:opacity-100"
            >
              <Play className="ml-0.5 h-4 w-4 text-white" fill="white" />
            </button>
          </div>
        )}

        <RatingBanner rating={audio.rating} />
      </div>

      <div className="card-body flex min-h-0 flex-1 flex-col gap-1.5 border-t border-border/50 px-2.5 pb-2 pt-2">
        <div>
          <p className="card-title line-clamp-2 font-semibold leading-snug text-foreground transition-colors group-hover:text-accent" title={audio.title}>
            {audio.title}
          </p>
          <div className="mt-1 flex items-center gap-2 text-[11px] text-muted">
            {audio.date && <span>{audio.date}</span>}
            {audio.studioName && (
              <span className="max-w-[120px] truncate" title={audio.studioName}>
                {audio.studioName}
              </span>
            )}
          </div>
          {performers.length > 0 && (
            <div className="mt-1.5 flex flex-wrap gap-1">
              {performers.slice(0, 4).map((performer) => (
                <button
                  key={performer.id}
                  onClick={(event) => {
                    event.stopPropagation();
                    onNavigate({ page: "performer", id: performer.id });
                  }}
                  className="max-w-[100px] truncate text-[11px] text-accent hover:underline"
                >
                  {performer.name}
                </button>
              ))}
              {performers.length > 4 && <span className="text-[10px] text-muted">+{performers.length - 4}</span>}
            </div>
          )}
        </div>
      </div>

      <hr className="my-0 border-border/50" />
      <div className="card-popovers flex min-h-[28px] flex-wrap items-center justify-center gap-1 rounded-b px-2 py-1.5">
        {!hasPopovers && <span className="select-none text-[10px] text-muted/30">&nbsp;</span>}
        {performers.length > 0 && (
          <PopoverButton icon={<User className="h-3.5 w-3.5" />} count={performers.length} title="Performers" wide preferBelow>
            <div className="grid grid-cols-2 gap-2">
              {performers.map((performer) => (
                <button
                  key={performer.id}
                  onClick={(event) => {
                    event.stopPropagation();
                    onNavigate({ page: "performer", id: performer.id });
                  }}
                  className="group/perf flex cursor-pointer flex-col items-center gap-1.5 rounded p-1.5 text-center transition-colors hover:bg-card-hover"
                >
                  <div className="h-28 w-20 flex-shrink-0 overflow-hidden rounded bg-surface">
                    {performer.imagePath ? (
                      <img src={performer.imagePath} alt="" className="h-full w-full object-cover" />
                    ) : (
                      <div className="flex h-full w-full items-center justify-center">
                        <User className="h-8 w-8 text-muted" />
                      </div>
                    )}
                  </div>
                  <span className="w-full truncate text-xs font-medium text-accent group-hover/perf:underline">
                    {performer.name}
                  </span>
                </button>
              ))}
            </div>
          </PopoverButton>
        )}
        {tags.length > 0 && (
          <PopoverButton icon={<Tag className="h-3.5 w-3.5" />} count={tags.length} title="Tags" preferBelow>
            <div className="flex flex-wrap gap-1">
              {tags.map((tag) => (
                <button
                  key={tag.id}
                  onClick={(event) => {
                    event.stopPropagation();
                    onNavigate({ page: "tag", id: tag.id });
                  }}
                  className="whitespace-nowrap rounded border border-border bg-card px-1.5 py-0.5 text-[11px] text-accent transition-colors hover:border-accent/40 hover:underline"
                >
                  {tag.name}
                </button>
              ))}
            </div>
          </PopoverButton>
        )}
        {groups.length > 0 && (
          <PopoverButton icon={<Layers className="h-3.5 w-3.5" />} count={groups.length} title="Groups" preferBelow>
            <div className="flex flex-col gap-0.5">
              {groups.map((group) => (
                <button
                  key={group.id}
                  onClick={(event) => {
                    event.stopPropagation();
                    onNavigate({ page: "group", id: group.id });
                  }}
                  className="truncate rounded px-2 py-1 text-left text-xs text-accent transition-colors hover:bg-card-hover hover:underline"
                >
                  {group.name}
                </button>
              ))}
            </div>
          </PopoverButton>
        )}
      </div>
    </div>
  );
}

export function AudioListRow({
  audio,
  onClick,
  onPlay,
  playing = false,
  selecting = false,
  selected = false,
  onSelect,
}: {
  audio: AudioSummary;
  onClick: () => void;
  onPlay?: () => void;
  playing?: boolean;
  selecting?: boolean;
  selected?: boolean;
  onSelect: () => void;
}) {
  return (
    <div
      className={`flex cursor-pointer items-center gap-3 border-b border-border/50 px-3 py-2 transition-colors hover:bg-card/50 ${
        playing ? "bg-accent/10" : ""
      } ${selected ? "bg-accent/5" : ""}`}
      onClick={selecting ? onSelect : onClick}
    >
      <input
        type="checkbox"
        checked={selected}
        onChange={onSelect}
        onClick={(event) => event.stopPropagation()}
        className={`h-4 w-4 flex-shrink-0 rounded accent-accent transition-opacity ${selected || selecting ? "opacity-100" : "opacity-60 hover:opacity-100"}`}
      />
      <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center overflow-hidden rounded bg-surface">
        {audio.coverImagePath ? (
          <img src={audio.coverImagePath} alt="" className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <Music className="h-4 w-4 text-muted/40" />
        )}
      </div>
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm font-medium text-foreground">{audio.title}</div>
        <div className="flex items-center gap-2 text-[11px] text-muted">
          {audio.date && <span>{audio.date}</span>}
          {audio.studioName && <span className="truncate">{audio.studioName}</span>}
        </div>
      </div>
      <div className="flex flex-shrink-0 items-center gap-3 text-xs text-muted">
        {audio.performerCount > 0 && (
          <span className="flex items-center gap-0.5">
            <User className="h-3 w-3" />
            {audio.performerCount}
          </span>
        )}
        {audio.tagCount > 0 && (
          <span className="flex items-center gap-0.5">
            <Tag className="h-3 w-3" />
            {audio.tagCount}
          </span>
        )}
        <span className="w-14 text-right tabular-nums">{formatDuration(audio.duration)}</span>
        {onPlay && (
          <button
            onClick={(event) => {
              event.stopPropagation();
              onPlay();
            }}
            className="rounded p-1 text-secondary hover:bg-card hover:text-accent"
          >
            <Play className="h-3.5 w-3.5" />
          </button>
        )}
      </div>
    </div>
  );
}
