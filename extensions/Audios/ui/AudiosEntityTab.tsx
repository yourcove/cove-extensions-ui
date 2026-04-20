import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Edit, Loader2, Music, Trash2 } from "lucide-react";
import {
  BulkEditDialog,
  ConfirmDialog,
  DetailListToolbar,
  FilterButton,
  FilterDialog,
  Pager,
  type FindFilter,
  useMultiSelect,
} from "@cove/runtime/components";
import {
  AUDIO_API,
  AUDIO_SORT_OPTIONS,
  AudioCard,
  apiFetch,
  fetchAudioList,
} from "./AudioListShared";
import {
  countActiveAudioFilters,
  filterAudioCriteria,
  omitHiddenAudioFilters,
  type AudioFilterField,
  type AudioObjectFilter,
} from "./AudioFilters";

interface AudiosEntityTabProps {
  entityId: number;
  filterParam: string;
  entityLabel: string;
  onNavigate: (route: any) => void;
}

export function AudiosEntityTab({ entityId, filterParam, entityLabel, onNavigate }: AudiosEntityTabProps) {
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<FindFilter>({ page: 1, perPage: 40, sort: "title", direction: "asc" });
  const [objectFilter, setObjectFilter] = useState<AudioObjectFilter>({});
  const [zoomLevel, setZoomLevel] = useState(0);
  const [filterDialogOpen, setFilterDialogOpen] = useState(false);
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [confirmBulkDelete, setConfirmBulkDelete] = useState(false);

  const hiddenFields = useMemo<AudioFilterField[]>(() => {
    switch (filterParam) {
      case "tagId":
        return ["tags"];
      case "performerId":
        return ["performers"];
      case "groupId":
        return ["groups"];
      case "studioId":
        return ["studios"];
      default:
        return [];
    }
  }, [filterParam]);

  const criteria = useMemo(() => filterAudioCriteria(hiddenFields), [hiddenFields]);
  const visibleObjectFilter = useMemo(() => omitHiddenAudioFilters(objectFilter, hiddenFields), [hiddenFields, objectFilter]);
  const activeFilterCount = countActiveAudioFilters(objectFilter, hiddenFields);

  const { data, isLoading } = useQuery({
    queryKey: ["audios", filterParam, entityId, filter, objectFilter],
    queryFn: () => fetchAudioList(filter, objectFilter, { [filterParam]: entityId }),
  });

  const { data: studiosData } = useQuery({
    queryKey: ["audio-bulk-studios"],
    enabled: showBulkEdit,
    queryFn: () => apiFetch<{ items: { id: number; name: string }[] }>("/api/studios?perPage=1000&sort=name&direction=asc"),
  });

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
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

  return (
    <div className="py-2">
      <div className="mb-3 flex items-center justify-end gap-2">
        {activeFilterCount > 0 && (
          <button
            onClick={() => {
              setObjectFilter({});
              setFilter((current) => ({ ...current, page: 1 }));
            }}
            className="text-xs text-secondary hover:text-foreground"
          >
            Clear filters
          </button>
        )}
        <FilterButton activeCount={activeFilterCount} onClick={() => setFilterDialogOpen(true)} />
      </div>

      <DetailListToolbar
        filter={filter}
        onFilterChange={setFilter}
        totalCount={totalCount}
        sortOptions={AUDIO_SORT_OPTIONS}
        zoomLevel={zoomLevel}
        onZoomChange={setZoomLevel}
        showSearch
        selectedCount={selectedIds.size}
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
      />

      {isLoading ? (
        <div className="flex items-center justify-center py-12">
          <div className="h-6 w-6 animate-spin rounded-full border-b-2 border-accent" />
        </div>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16 text-center text-muted/30">
          <Music className="h-12 w-12" />
          <p className="mt-3 text-sm text-secondary">
            {filter.q || activeFilterCount > 0
              ? "No audio files match the current search and filters"
              : `No audio files linked to this ${entityLabel}`}
          </p>
        </div>
      ) : (
        <div className="grid gap-4" style={{ gridTemplateColumns: "repeat(auto-fill, minmax(var(--card-min-width, 240px), 1fr))" }}>
          {items.map((audio) => (
            <AudioCard
              key={audio.id}
              audio={audio}
              onClick={() => (selectedIds.size > 0 ? toggle(audio.id) : onNavigate({ page: "audio", id: audio.id }))}
              onNavigate={onNavigate}
              selecting={selectedIds.size > 0}
              selected={selectedIds.has(audio.id)}
              onSelect={() => toggle(audio.id)}
            />
          ))}
        </div>
      )}

      <Pager filter={filter} setFilter={setFilter} totalCount={totalCount} />

      <FilterDialog
        open={filterDialogOpen}
        onClose={() => setFilterDialogOpen(false)}
        criteria={criteria}
        activeFilter={visibleObjectFilter}
        onApply={(nextFilter) => {
          setObjectFilter(nextFilter as AudioObjectFilter);
          setFilter((current) => ({ ...current, page: 1 }));
        }}
      />

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
