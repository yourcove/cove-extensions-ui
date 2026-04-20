import { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Play, Pause, Trash2, User, Layers, Music,
  ArrowLeft, Check, Eye,
} from "lucide-react";
import {
  InteractiveRating,
  ConfirmDialog,
  ImageInput,
  TagBadge,
  formatDuration,
  formatFileSize,
  formatDate,
} from "@cove/runtime/components";
import { AudioPlayer } from "./AudioPlayer";

interface AudioDetail {
  id: number; title: string; path: string;
  artist?: string; album?: string; genre?: string; trackNumber?: number; year?: number;
  duration?: number; bitrate?: number; sampleRate?: number; channels?: number; fileSize?: number;
  coverImagePath?: string; rating: number; playCount: number; organized: boolean; date?: string;
  studioId?: number; tagIds: number[]; performerIds: number[]; groupIds: number[];
  createdAt: string; updatedAt: string;
}
interface EntityRef { id: number; name: string; image_path?: string; disambiguation?: string; }

const API = "/api/ext/audios";

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, init);
  if (!res.ok) throw new Error(`${res.status}`);
  if (res.status === 204) return undefined as T;
  return res.json();
}

// ── Entity selector for edit panel ───────────────────────────────

function EntitySelector({ label, selectedIds, allItems, search, onSearchChange, onAdd, onRemove, pillClassName, getName }: {
  label: string; selectedIds: number[]; allItems: EntityRef[]; search: string;
  onSearchChange: (v: string) => void; onAdd: (id: number) => void; onRemove: (id: number) => void;
  pillClassName: string; getName: (item: EntityRef) => string;
}) {
  const selected = allItems.filter((item) => selectedIds.includes(item.id));
  const filtered = allItems.filter((item) => !selectedIds.includes(item.id) && getName(item).toLowerCase().includes(search.toLowerCase()));
  const inputCls = "w-full bg-input border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent";
  return (
    <div className="space-y-1">
      <span className="text-xs text-secondary">{label}</span>
      <div className="flex flex-wrap gap-1 mb-1">
        {selected.map((item) => (
          <span key={item.id} className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs ${pillClassName}`}>
            {getName(item)}<button onClick={() => onRemove(item.id)} className="hover:text-white">&times;</button>
          </span>
        ))}
      </div>
      <input value={search} onChange={(e) => onSearchChange(e.target.value)} placeholder={`Search ${label.toLowerCase()}\u2026`} className={inputCls} />
      {search && filtered.length > 0 && (
        <div className="max-h-32 overflow-y-auto bg-surface rounded border border-border">
          {filtered.slice(0, 10).map((item) => (
            <button key={item.id} onClick={() => { onAdd(item.id); onSearchChange(""); }}
              className="block w-full text-left px-3 py-1 text-sm text-foreground hover:bg-card">{getName(item)}</button>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Edit panel ───────────────────────────────────────────────────

function EditPanel({ audio, onSaved }: { audio: AudioDetail; onSaved: () => void }) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState(audio.title || "");
  const [date, setDate] = useState(audio.date || "");
  const [studioId, setStudioId] = useState<number | undefined>(audio.studioId ?? undefined);
  const [rating, setRating] = useState<number>(audio.rating);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(audio.tagIds);
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(audio.performerIds);
  const [selectedGroupIds, setSelectedGroupIds] = useState<number[]>(audio.groupIds);
  const [tagSearch, setTagSearch] = useState("");
  const [perfSearch, setPerfSearch] = useState("");
  const [groupSearch, setGroupSearch] = useState("");
  const [studioSearch, setStudioSearch] = useState("");

  const { data: allTags } = useQuery({ queryKey: ["tags-all"], queryFn: () => apiFetch<{ items: EntityRef[] }>("/api/tags?perPage=500&sort=name&direction=asc") });
  const { data: allPerformers } = useQuery({ queryKey: ["performers-all"], queryFn: () => apiFetch<{ items: EntityRef[] }>("/api/performers?perPage=500&sort=name&direction=asc") });
  const { data: allStudios } = useQuery({ queryKey: ["studios-all"], queryFn: () => apiFetch<{ items: EntityRef[] }>("/api/studios?perPage=500&sort=name&direction=asc") });
  const { data: allGroups } = useQuery({ queryKey: ["groups-all"], queryFn: () => apiFetch<{ items: EntityRef[] }>("/api/groups?perPage=500&sort=name&direction=asc") });

  useEffect(() => {
    setTitle(audio.title || ""); setDate(audio.date || "");
    setStudioId(audio.studioId ?? undefined); setRating(audio.rating);
    setSelectedTagIds(audio.tagIds); setSelectedPerformerIds(audio.performerIds); setSelectedGroupIds(audio.groupIds);
  }, [audio]);

  const mutation = useMutation({
    mutationFn: (data: any) => apiFetch(`${API}/${audio.id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(data) }),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["audio", audio.id] }); queryClient.invalidateQueries({ queryKey: ["audios"] }); onSaved(); },
  });
  const handleSave = () => {
    mutation.mutate({ title: title || undefined, date: date || undefined,
      studioId, rating,
      tagIds: selectedTagIds, performerIds: selectedPerformerIds, groupIds: selectedGroupIds });
  };

  const inputCls = "w-full bg-input border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent";
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <label className="space-y-1"><span className="text-xs text-secondary">Title</span><input value={title} onChange={(e) => setTitle(e.target.value)} className={inputCls} /></label>
        <label className="space-y-1"><span className="text-xs text-secondary">Date</span><input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} /></label>
      </div>
      <div className="space-y-1">
        <span className="text-xs text-secondary">Studio</span>
        {studioId && allStudios?.items.find((s) => s.id === studioId) && (
          <div className="flex items-center gap-1 mb-1">
            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-accent/20 text-accent">
              {allStudios.items.find((s) => s.id === studioId)!.name}
              <button onClick={() => setStudioId(undefined)} className="hover:text-white">&times;</button>
            </span>
          </div>
        )}
        {!studioId && (
          <>
            <input value={studioSearch} onChange={(e) => setStudioSearch(e.target.value)} placeholder="Search studios\u2026" className={inputCls} />
            {studioSearch && allStudios && (
              <div className="max-h-24 overflow-y-auto bg-surface rounded border border-border">
                {allStudios.items.filter((s) => s.name.toLowerCase().includes(studioSearch.toLowerCase())).slice(0, 10).map((s) => (
                  <button key={s.id} onClick={() => { setStudioId(s.id); setStudioSearch(""); }} className="block w-full text-left px-3 py-1 text-sm text-foreground hover:bg-card">{s.name}</button>
                ))}
              </div>
            )}
          </>
        )}
      </div>
      <EntitySelector label="Tags" selectedIds={selectedTagIds} allItems={allTags?.items ?? []} search={tagSearch}
        onSearchChange={setTagSearch} onAdd={(id) => setSelectedTagIds([...selectedTagIds, id])}
        onRemove={(id) => setSelectedTagIds(selectedTagIds.filter((i) => i !== id))} pillClassName="bg-accent/20 text-accent" getName={(t) => t.name} />
      <EntitySelector label="Performers" selectedIds={selectedPerformerIds} allItems={allPerformers?.items ?? []} search={perfSearch}
        onSearchChange={setPerfSearch} onAdd={(id) => setSelectedPerformerIds([...selectedPerformerIds, id])}
        onRemove={(id) => setSelectedPerformerIds(selectedPerformerIds.filter((i) => i !== id))} pillClassName="bg-accent/10 text-accent-hover"
        getName={(p) => p.disambiguation ? `${p.name} (${p.disambiguation})` : p.name} />
      <EntitySelector label="Groups" selectedIds={selectedGroupIds} allItems={allGroups?.items ?? []} search={groupSearch}
        onSearchChange={setGroupSearch} onAdd={(id) => setSelectedGroupIds([...selectedGroupIds, id])}
        onRemove={(id) => setSelectedGroupIds(selectedGroupIds.filter((i) => i !== id))} pillClassName="bg-accent/10 text-accent" getName={(g) => g.name} />
      <ImageInput
        currentImageUrl={audio.coverImagePath}
        onUpload={async (file) => {
          const formData = new FormData();
          formData.append("file", file);
          const res = await fetch(`${API}/${audio.id}/cover`, { method: "POST", body: formData });
          if (!res.ok) throw new Error(`${res.status}`);
          return res.json();
        }}
        onDelete={audio.coverImagePath ? async () => {
          const res = await fetch(`${API}/${audio.id}/cover`, { method: "DELETE" });
          if (!res.ok && res.status !== 204) throw new Error(`${res.status}`);
        } : undefined}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ["audio", audio.id] });
          queryClient.invalidateQueries({ queryKey: ["audios"] });
        }}
        label="Cover Image"
        aspectRatio="1/1"
        className="w-40"
      />
      {mutation.error && (
        <div className="bg-red-900/50 border border-red-700 text-red-300 rounded p-2 text-sm">
          {(mutation.error as Error).message}
        </div>
      )}
      <div className="flex justify-end gap-3 pt-2">
        <button onClick={onSaved} className="px-4 py-2 text-sm text-secondary hover:text-foreground">Cancel</button>
        <button onClick={handleSave} disabled={mutation.isPending} className="px-4 py-2 text-sm bg-accent hover:bg-accent-hover text-white rounded disabled:opacity-50">
          {mutation.isPending ? "Saving\u2026" : "Save"}
        </button>
      </div>
    </div>
  );
}

// ── PerformerCard (matches SceneDetailPage pattern) ──────────────

function PerformerCard({ performer, onClick }: { performer: EntityRef; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className="bg-card border border-border rounded overflow-hidden hover:border-accent/60 transition-colors text-left"
      style={{ width: "200px" }}
    >
      <div className="aspect-[2/3] bg-surface flex items-center justify-center relative">
        {performer.image_path ? (
          <img src={performer.image_path} alt={performer.name} className="w-full h-full object-cover" />
        ) : (
          <div className="w-full h-full flex items-center justify-center bg-gradient-to-b from-card to-surface">
            <svg viewBox="0 0 100 150" className="w-2/3 h-2/3 opacity-30">
              <ellipse cx="50" cy="35" rx="25" ry="30" fill="currentColor" className="text-muted"/>
              <ellipse cx="50" cy="120" rx="40" ry="45" fill="currentColor" className="text-muted"/>
            </svg>
          </div>
        )}
      </div>
      <div className="p-2 text-center">
        <div className="text-sm text-foreground font-medium truncate">{performer.name}</div>
      </div>
    </button>
  );
}

type TabKey = "details" | "edit" | "file-info";

export function AudioDetailPage({ id, onNavigate }: { id: number; onNavigate: (r: any) => void }) {
  const queryClient = useQueryClient();
  const [playing, setPlaying] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("details");
  const [confirmDelete, setConfirmDelete] = useState(false);

  const { data: audio, isLoading, error } = useQuery({ queryKey: ["audio", id], queryFn: () => apiFetch<AudioDetail>(`${API}/${id}`) });

  const { data: resolvedTags } = useQuery({
    queryKey: ["audio-tags", audio?.tagIds], enabled: !!audio && audio.tagIds.length > 0,
    queryFn: async () => { const r: EntityRef[] = []; for (const tid of audio!.tagIds) { try { const t = await apiFetch<any>(`/api/tags/${tid}`); r.push({ id: t.id, name: t.name }); } catch {} } return r; },
  });
  const { data: resolvedPerformers } = useQuery({
    queryKey: ["audio-performers", audio?.performerIds], enabled: !!audio && audio.performerIds.length > 0,
    queryFn: async () => { const r: EntityRef[] = []; for (const pid of audio!.performerIds) { try { const p = await apiFetch<any>(`/api/performers/${pid}`); r.push({ id: p.id, name: p.name, image_path: p.imagePath }); } catch {} } return r; },
  });
  const { data: resolvedGroups } = useQuery({
    queryKey: ["audio-groups", audio?.groupIds], enabled: !!audio && audio.groupIds.length > 0,
    queryFn: async () => { const r: EntityRef[] = []; for (const gid of audio!.groupIds) { try { const g = await apiFetch<any>(`/api/groups/${gid}`); r.push({ id: g.id, name: g.name, image_path: g.frontImagePath }); } catch {} } return r; },
  });
  const { data: resolvedStudio } = useQuery({
    queryKey: ["audio-studio", audio?.studioId], enabled: !!audio?.studioId,
    queryFn: () => apiFetch<any>(`/api/studios/${audio!.studioId}`),
  });

  const updateMut = useMutation({
    mutationFn: (data: any) => apiFetch(`${API}/${id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(data) }),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["audio", id] }); queryClient.invalidateQueries({ queryKey: ["audios"] }); },
  });
  const deleteMut = useMutation({
    mutationFn: (deleteFile?: boolean) => fetch(`${API}/${id}${deleteFile ? "?deleteFile=true" : ""}`, { method: "DELETE" }),
    onSuccess: () => onNavigate({ page: "audios" }),
  });

  useEffect(() => {
    if (audio) document.title = `${audio.title} | Audios | Cove`;
    return () => { document.title = "Cove"; };
  }, [audio]);

  useEffect(() => {
    const main = document.querySelector("main");
    if (main) { main.style.overflowX = "hidden"; }
    return () => { if (main) main.style.overflowX = ""; };
  }, []);

  if (isLoading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent" /></div>;
  if (error || !audio) return (
    <div className="text-center text-secondary py-16"><p>Audio not found</p>
      <button onClick={() => onNavigate({ page: "audios" })} className="mt-4 px-3 py-1.5 rounded text-xs bg-accent text-white hover:bg-accent/80">Back to Audios</button>
    </div>
  );

  const tabs: { key: TabKey; label: string }[] = [
    { key: "details", label: "Details" },
    { key: "file-info", label: "File Info" },
    { key: "edit", label: "Edit" },
  ];

  const handlePlayToggle = () => {
    if (!playing) {
      fetch(`${API}/${id}/play`, { method: "POST" }).catch(() => {});
    }
    setPlaying(!playing);
  };

  return (
    <div className="-mx-6 -mt-5 -mb-5">
      <div className="flex flex-col xl:flex-row xl:h-[calc(100vh-48px)]">
        {/* Left sidebar */}
        <div className="w-full xl:w-[400px] 2xl:w-[450px] xl:min-w-[350px] xl:max-w-[500px] xl:border-r border-b xl:border-b-0 border-border overflow-y-auto shrink-0 xl:max-h-[calc(100vh-48px)]">
          <div className="px-6 pt-4 pb-2">
            <button onClick={() => onNavigate({ page: "audios" })} className="flex items-center gap-1 text-xs text-secondary hover:text-accent transition-colors mb-3">
              <ArrowLeft className="w-3.5 h-3.5" /> Back to Audios
            </button>
            {resolvedStudio && (
              <button onClick={() => onNavigate({ page: "studio", id: resolvedStudio.id })} className="text-accent hover:underline text-sm mb-2 block">{resolvedStudio.name}</button>
            )}
            <h3 className="text-2xl font-semibold text-foreground leading-snug line-clamp-2">{audio.title}</h3>
            <div className="flex items-center justify-between mt-2 text-sm text-secondary">
              <span>{audio.date || ""}</span>
              <span className="flex items-center gap-1.5">
                {audio.duration != null && audio.duration > 0 && <span>{formatDuration(audio.duration)}</span>}
              </span>
            </div>
            <div className="flex items-center justify-between mt-3 gap-2">
              <InteractiveRating value={audio.rating} onChange={(value) => updateMut.mutate({ rating: value })} />
              <div className="flex items-center gap-2">
                <span className="flex items-center gap-1 text-sm text-secondary" title="Play count">
                  <Eye className="w-4 h-4" /><span>{audio.playCount}</span>
                </span>
                <button onClick={() => updateMut.mutate({ organized: !audio.organized })}
                  className={`p-1 rounded ${audio.organized ? "bg-green-600 text-white" : "bg-card text-muted hover:text-foreground"}`}
                  title={audio.organized ? "Organized" : "Not organized"}>
                  <Check className="w-4 h-4" />
                </button>
                <button onClick={() => setConfirmDelete(true)}
                  className="p-1 rounded text-secondary hover:text-red-400 hover:bg-card" title="Delete">
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          </div>
          <div className="px-6">
            <div className="flex flex-wrap border-b border-border">
              {tabs.map((tab) => (
                <button key={tab.key} onClick={() => setActiveTab(tab.key)}
                  className={`px-2.5 py-2 text-sm transition-colors border-b-2 cursor-pointer ${activeTab === tab.key ? "border-accent text-accent" : "border-transparent text-secondary hover:text-foreground"}`}>
                  {tab.label}
                </button>
              ))}
            </div>
          </div>
          <div className="px-6 py-4">
            {activeTab === "details" && (
              <div className="space-y-4">
                <dl className="grid gap-y-1.5 text-sm" style={{ gridTemplateColumns: "auto 1fr" }}>
                  <dt className="text-muted pr-3">Created</dt><dd className="text-foreground">{formatDate(audio.createdAt)}</dd>
                  <dt className="text-muted pr-3">Updated</dt><dd className="text-foreground">{formatDate(audio.updatedAt)}</dd>
                  {audio.artist && <><dt className="text-muted pr-3">Artist</dt><dd className="text-foreground">{audio.artist}</dd></>}
                  {audio.album && <><dt className="text-muted pr-3">Album</dt><dd className="text-foreground">{audio.album}</dd></>}
                  {audio.genre && <><dt className="text-muted pr-3">Genre</dt><dd className="text-foreground">{audio.genre}</dd></>}
                  {audio.trackNumber != null && <><dt className="text-muted pr-3">Track</dt><dd className="text-foreground">{audio.trackNumber}</dd></>}
                  {audio.year != null && <><dt className="text-muted pr-3">Year</dt><dd className="text-foreground">{audio.year}</dd></>}
                  {audio.duration != null && audio.duration > 0 && <><dt className="text-muted pr-3">Duration</dt><dd className="text-foreground">{formatDuration(audio.duration)}</dd></>}
                  <dt className="text-muted pr-3">Play Count</dt><dd className="text-foreground">{audio.playCount}</dd>
                  <dt className="text-muted pr-3">Organized</dt><dd className="text-foreground">{audio.organized ? "Yes" : "No"}</dd>
                </dl>

                {/* Tags */}
                {(resolvedTags ?? []).length > 0 && (
                  <div>
                    <h6 className="text-sm text-muted mb-2">Tags</h6>
                    <div className="flex flex-wrap gap-1.5">
                      {resolvedTags!.map((tag) => (
                        <TagBadge key={tag.id} name={tag.name} onClick={() => onNavigate({ page: "tag", id: tag.id })} />
                      ))}
                    </div>
                  </div>
                )}

                {/* Performers — matches SceneDetailPage pattern */}
                {(resolvedPerformers ?? []).length > 0 && (
                  <div>
                    <h6 className="text-sm text-muted mb-2">Performer{resolvedPerformers!.length > 1 ? "s" : ""}</h6>
                    <div className="flex flex-wrap gap-3">
                      {resolvedPerformers!.map((p) => <PerformerCard key={p.id} performer={p} onClick={() => onNavigate({ page: "performer", id: p.id })} />)}
                    </div>
                  </div>
                )}

                {/* Groups */}
                {(resolvedGroups ?? []).length > 0 && (
                  <div>
                    <h6 className="text-sm text-muted mb-2">Group{resolvedGroups!.length > 1 ? "s" : ""}</h6>
                    <div className="flex flex-wrap gap-3">
                      {resolvedGroups!.map((g) => (
                        <button key={g.id} onClick={() => onNavigate({ page: "group", id: g.id })}
                          className="bg-card border border-border rounded overflow-hidden hover:border-accent/60 transition-colors text-left" style={{ width: "160px" }}>
                          <div className="aspect-square bg-surface flex items-center justify-center">
                            {g.image_path ? <img src={g.image_path} alt="" className="w-full h-full object-cover" /> : <Layers className="w-10 h-10 text-muted/30" />}
                          </div>
                          <div className="p-2 text-center">
                            <span className="text-xs font-medium text-foreground truncate block">{g.name}</span>
                          </div>
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
            {activeTab === "file-info" && (
              <dl className="grid gap-y-1.5 text-sm" style={{ gridTemplateColumns: "auto 1fr" }}>
                <dt className="text-muted pr-3">Path</dt><dd className="text-foreground break-all font-mono text-xs">{audio.path}</dd>
                <dt className="text-muted pr-3">File Size</dt><dd className="text-foreground">{formatFileSize(audio.fileSize)}</dd>
                {audio.artist && <><dt className="text-muted pr-3">Artist</dt><dd className="text-foreground">{audio.artist}</dd></>}
                {audio.album && <><dt className="text-muted pr-3">Album</dt><dd className="text-foreground">{audio.album}</dd></>}
                {audio.genre && <><dt className="text-muted pr-3">Genre</dt><dd className="text-foreground">{audio.genre}</dd></>}
                {audio.bitrate != null && <><dt className="text-muted pr-3">Bit Rate</dt><dd className="text-foreground">{audio.bitrate} kbps</dd></>}
                {audio.sampleRate != null && <><dt className="text-muted pr-3">Sample Rate</dt><dd className="text-foreground">{audio.sampleRate} Hz</dd></>}
                {audio.channels != null && <><dt className="text-muted pr-3">Channels</dt><dd className="text-foreground">{audio.channels}</dd></>}
                {audio.trackNumber != null && <><dt className="text-muted pr-3">Track</dt><dd className="text-foreground">{audio.trackNumber}</dd></>}
                {audio.year != null && <><dt className="text-muted pr-3">Year</dt><dd className="text-foreground">{audio.year}</dd></>}
                {audio.duration != null && audio.duration > 0 && <><dt className="text-muted pr-3">Duration</dt><dd className="text-foreground">{formatDuration(audio.duration)}</dd></>}
              </dl>
            )}
            {activeTab === "edit" && <EditPanel audio={audio} onSaved={() => setActiveTab("details")} />}
          </div>
        </div>

        {/* Right panel: cover art + play button */}
        <div className="flex-1 flex flex-col items-center justify-center bg-background min-h-[300px] xl:min-h-0 overflow-hidden">
          <div className="flex flex-col items-center justify-center flex-1 p-8 gap-6 w-full max-w-lg">
            <div className="w-64 h-64 rounded-xl overflow-hidden bg-surface border border-border shadow-lg">
              {audio.coverImagePath ? <img src={audio.coverImagePath} alt="" className="w-full h-full object-cover" />
                : <div className="w-full h-full flex items-center justify-center bg-gradient-to-br from-surface to-card"><Music className="w-24 h-24 text-muted/20" /></div>}
            </div>
            <button onClick={handlePlayToggle}
              className="flex items-center gap-2 px-6 py-3 rounded-full bg-accent text-white text-sm font-medium hover:bg-accent-hover transition-colors shadow-lg">
              {playing ? <Pause className="w-5 h-5" /> : <Play className="w-5 h-5" />}
              {playing ? "Stop" : "Play"}
            </button>
            <div className="text-center space-y-1">
              <p className="text-lg font-semibold text-foreground">{audio.title}</p>
              {audio.date && <p className="text-sm text-secondary">{audio.date}</p>}
            </div>
          </div>
        </div>
      </div>

      {/* Audio player overlay */}
      {playing && <div className="fixed bottom-0 left-0 right-0 z-50"><AudioPlayer audioId={id} onClose={() => setPlaying(false)} /></div>}

      <ConfirmDialog
        open={confirmDelete}
        title="Delete Audio"
        message="Are you sure you want to delete this audio file? This action cannot be undone."
        confirmLabel="Delete"
        onConfirm={(opts) => { setConfirmDelete(false); deleteMut.mutate(opts?.deleteFile); }}
        onCancel={() => setConfirmDelete(false)}
        showDeleteFile
        destructive
      />
    </div>
  );
}
