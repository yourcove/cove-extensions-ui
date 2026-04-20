import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Edit, Loader2, Music, Trash2 } from "lucide-react";
import {
  BulkEditDialog,
  ConfirmDialog,
  ListPage,
  getDefaultFilter,
  type DisplayMode,
  useListUrlState,
  useMultiSelect,
} from "@cove/runtime/components";
import { AudioPlayer } from "./AudioPlayer";
import {
  AUDIO_API,
  AUDIO_SORT_OPTIONS,
  AudioCard,
  AudioListRow,
  apiFetch,
  fetchAudioList,
} from "./AudioListShared";
import { AUDIO_CRITERIA, type AudioObjectFilter } from "./AudioFilters";

export function AudiosPage({ onNavigate }: { onNavigate: (route: any) => void }) {
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("audios");
    const savedDisplayMode = savedFilter?.uiOptions?.displayMode === "list" ? "list" : "grid";

    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, sort: "title", direction: "asc" as const },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: savedDisplayMode as DisplayMode,
    };
  }, []);

  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "audios",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
  });

  const queryClient = useQueryClient();
  const [playingId, setPlayingId] = useState<number | null>(null);
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [confirmBulkDelete, setConfirmBulkDelete] = useState(false);

  const typedObjectFilter = objectFilter as AudioObjectFilter;
  const hasObjectFilter = Object.keys(typedObjectFilter).length > 0;

  const { data, isLoading } = useQuery({
    queryKey: ["audios", filter, typedObjectFilter],
    queryFn: () => fetchAudioList(filter, typedObjectFilter),
  });

  const { data: studiosData } = useQuery({
    queryKey: ["audio-bulk-studios"],
    enabled: showBulkEdit,
    queryFn: () => apiFetch<{ items: { id: number; name: string }[] }>("/api/studios?perPage=1000&sort=name&direction=asc"),
  });

  const items = data?.items ?? [];
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(items);
  const studioOptions = useMemo(
    () => [
      { label: "None", value: 0 },
      ...(studiosData?.items ?? []).map((studio) => ({ label: studio.name, value: studio.id })),
    ],
    [studiosData],
  );

  const bulkDeleteMut = useMutation({
    mutationFn: () =>
      apiFetch(`${AUDIO_API}/bulk-delete`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ids: [...selectedIds] }),
      }),
    onSuccess: () => {
      setConfirmBulkDelete(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["audios"] });
      queryClient.invalidateQueries({ queryKey: ["audio"] });
    },
  });

  const bulkEditMut = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      apiFetch(`${AUDIO_API}/bulk-update`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ids: [...selectedIds], ...values }),
      }),
    onSuccess: () => {
      setShowBulkEdit(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["audios"] });
      queryClient.invalidateQueries({ queryKey: ["audio"] });
    },
  });

  const handlePlay = async (audioId: number) => {
    setPlayingId(audioId);
    try {
      await fetch(`${AUDIO_API}/${audioId}/play`, { method: "POST" });
    } catch {
      // Keep the player state responsive even if the play-count mutation fails.
    }
  };

  useEffect(() => {
    document.title = "Audios | Cove";
    return () => {
      document.title = "Cove";
    };
  }, []);

  return (
    <div className={playingId != null ? "pb-24" : undefined}>
      <ListPage
        title="Audios"
        filterMode="audios"
        filter={filter}
        onFilterChange={setFilter}
        totalCount={data?.totalCount ?? 0}
        isLoading={isLoading}
        sortOptions={AUDIO_SORT_OPTIONS}
        displayMode={displayMode}
        onDisplayModeChange={setDisplayMode}
        availableDisplayModes={["grid", "list"]}
        criteriaDefinitions={AUDIO_CRITERIA}
        objectFilter={typedObjectFilter}
        onObjectFilterChange={setObjectFilter}
        selectedIds={selectedIds}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        selectionActions={
          <>
            <button
              onClick={() => setShowBulkEdit(true)}
              className="flex items-center gap-1 rounded px-2 py-0.5 text-xs text-accent hover:bg-accent/10 hover:text-accent-hover"
            >
              <Edit className="h-3 w-3" />
              Edit
            </button>
            <button
              onClick={() => setConfirmBulkDelete(true)}
              disabled={bulkDeleteMut.isPending}
              className="flex items-center gap-1 rounded px-2 py-0.5 text-xs text-red-400 hover:bg-red-900/20 hover:text-red-300 disabled:opacity-50"
            >
              {bulkDeleteMut.isPending ? <Loader2 className="h-3 w-3 animate-spin" /> : <Trash2 className="h-3 w-3" />}
              Delete
            </button>
          </>
        }
      >
        {items.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <Music className="mb-4 h-16 w-16 text-muted/20" />
            <p className="mb-2 text-lg text-secondary">
              {hasObjectFilter ? "No audio files match the current filters" : "No audio files found"}
            </p>
            <p className="max-w-md text-sm text-muted">
              {hasObjectFilter
                ? "Try clearing one or more filters, or broaden your search query."
                : "Configure audio scan extensions in Settings -> Library -> Extensions, then run a scan."}
            </p>
          </div>
        ) : displayMode === "grid" ? (
          <div className="grid gap-3" style={{ gridTemplateColumns: "repeat(auto-fill, minmax(var(--card-min-width, 240px), 1fr))" }}>
            {items.map((audio) => (
              <AudioCard
                key={audio.id}
                audio={audio}
                onClick={() => onNavigate({ page: "audio", id: audio.id })}
                onNavigate={onNavigate}
                onPlay={() => void handlePlay(audio.id)}
                playing={playingId === audio.id}
                selecting={selectedIds.size > 0}
                selected={selectedIds.has(audio.id)}
                onSelect={() => toggle(audio.id)}
              />
            ))}
          </div>
        ) : (
          <div>
            <div className="flex items-center gap-3 border-b border-border px-3 py-1.5 text-[11px] font-medium uppercase tracking-wide text-muted">
              {selectedIds.size > 0 && <div className="w-4 flex-shrink-0" />}
              <div className="w-9 flex-shrink-0" />
              <div className="flex-1">Title</div>
              <div className="w-20 text-right">Performers</div>
              <div className="w-16 text-right">Tags</div>
              <div className="w-14 text-right">Duration</div>
              <div className="w-8" />
            </div>
            {items.map((audio) => (
              <AudioListRow
                key={audio.id}
                audio={audio}
                onClick={() => onNavigate({ page: "audio", id: audio.id })}
                onPlay={() => void handlePlay(audio.id)}
                playing={playingId === audio.id}
                selecting={selectedIds.size > 0}
                selected={selectedIds.has(audio.id)}
                onSelect={() => toggle(audio.id)}
              />
            ))}
          </div>
        )}
      </ListPage>

      {playingId !== null && (
        <div className="fixed bottom-0 left-0 right-0 z-50">
          <AudioPlayer
            audioId={playingId}
            onClose={() => setPlayingId(null)}
            onNext={() => {
              const currentIndex = items.findIndex((audio) => audio.id === playingId);
              if (currentIndex >= 0 && currentIndex < items.length - 1) {
                void handlePlay(items[currentIndex + 1].id);
              }
            }}
            onPrevious={() => {
              const currentIndex = items.findIndex((audio) => audio.id === playingId);
              if (currentIndex > 0) {
                void handlePlay(items[currentIndex - 1].id);
              }
            }}
            compact
          />
        </div>
      )}

      <BulkEditDialog
        open={showBulkEdit}
        onClose={() => setShowBulkEdit(false)}
        title="Edit Audios"
        selectedCount={selectedIds.size}
        fields={[
          { key: "rating", label: "Rating", type: "number" },
          { key: "organized", label: "Organized", type: "bool" },
          { key: "studioId", label: "Studio", type: "select", options: studioOptions },
          { key: "tagIds", label: "Tags", type: "multiId", entityType: "tags" },
          { key: "performerIds", label: "Performers", type: "multiId", entityType: "performers" },
          { key: "groupIds", label: "Groups", type: "multiId", entityType: "groups" },
        ]}
        onApply={(values) => bulkEditMut.mutate(values)}
        isPending={bulkEditMut.isPending}
      />

      <ConfirmDialog
        open={confirmBulkDelete}
        title="Delete Audios"
        message={`Delete ${selectedIds.size} audio file${selectedIds.size === 1 ? "" : "s"}? This cannot be undone.`}
        confirmLabel="Delete"
        onConfirm={() => bulkDeleteMut.mutate()}
        onCancel={() => setConfirmBulkDelete(false)}
        destructive
      />
    </div>
  );
}
