import { useQuery } from "@tanstack/react-query";
import { Music } from "lucide-react";
import { PopoverButton, formatDuration, type FindFilter } from "@cove/runtime/components";
import { fetchAudioList } from "./AudioListShared";

interface FooterBaseProps {
  entityId: number;
  filterParam: string;
  onNavigate?: (route: any) => void;
}

function AudiosCardFooterBase({ entityId, filterParam, onNavigate }: FooterBaseProps) {
  const filter: FindFilter = { page: 1, perPage: 6, sort: "title", direction: "asc" };
  const { data } = useQuery({
    queryKey: ["audio-card-footer", filterParam, entityId],
    queryFn: () => fetchAudioList(filter, {}, { [filterParam]: entityId }),
  });

  if (!data || data.totalCount <= 0) {
    return null;
  }

  return (
    <PopoverButton icon={<Music className="h-3 w-3" />} count={data.totalCount} title="Audios" wide preferBelow>
      <div className="w-72 space-y-1.5">
        {data.items.map((audio) => (
          <button
            key={audio.id}
            onClick={(event) => {
              event.stopPropagation();
              onNavigate?.({ page: "audio", id: audio.id });
            }}
            className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left transition-colors hover:bg-card-hover"
          >
            <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center overflow-hidden rounded bg-surface">
              {audio.coverImagePath ? (
                <img src={audio.coverImagePath} alt="" className="h-full w-full object-cover" loading="lazy" />
              ) : (
                <Music className="h-4 w-4 text-muted/40" />
              )}
            </div>
            <div className="min-w-0 flex-1">
              <div className="truncate text-sm font-medium text-foreground">{audio.title}</div>
              <div className="text-[11px] text-muted">
                {audio.date || "Unknown date"}
                {audio.duration != null && audio.duration > 0 ? ` · ${formatDuration(audio.duration)}` : ""}
              </div>
            </div>
          </button>
        ))}
      </div>
    </PopoverButton>
  );
}

export function TagAudiosCardFooter({ tag, onNavigate }: { tag: { id: number }; onNavigate?: (route: any) => void }) {
  return <AudiosCardFooterBase entityId={tag.id} filterParam="tagId" onNavigate={onNavigate} />;
}

export function StudioAudiosCardFooter({ studio, onNavigate }: { studio: { id: number }; onNavigate?: (route: any) => void }) {
  return <AudiosCardFooterBase entityId={studio.id} filterParam="studioId" onNavigate={onNavigate} />;
}

export function GroupAudiosCardFooter({ group, onNavigate }: { group: { id: number }; onNavigate?: (route: any) => void }) {
  return <AudiosCardFooterBase entityId={group.id} filterParam="groupId" onNavigate={onNavigate} />;
}
