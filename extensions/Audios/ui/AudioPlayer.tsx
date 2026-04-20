import { useState, useRef, useEffect, useCallback } from "react";
import {
  Play, Pause, SkipBack, SkipForward, Volume2, VolumeX,
  X, Music, RotateCcw, RotateCw, SlidersHorizontal,
} from "lucide-react";

const API = "/api/ext/audios";
const SPEED_OPTIONS = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];

interface AudioPlayerProps {
  audioId: number;
  onClose: () => void;
  onNext?: () => void;
  onPrevious?: () => void;
  compact?: boolean;
}

function formatTime(seconds: number): string {
  if (!seconds || seconds <= 0) return "0:00";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (h > 0) return `${h}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
  return `${m}:${s.toString().padStart(2, "0")}`;
}

export function AudioPlayer({ audioId, onClose, onNext, onPrevious, compact }: AudioPlayerProps) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const progressRef = useRef<HTMLDivElement>(null);

  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(0.8);
  const [muted, setMuted] = useState(false);
  const [speed, setSpeed] = useState(1);
  const [showSpeed, setShowSpeed] = useState(false);
  const [showFilters, setShowFilters] = useState(false);
  const [pitchCorrection, setPitchCorrection] = useState(true);
  const [pitchSemitones, setPitchSemitones] = useState(0);
  const [title, setTitle] = useState("");
  const [coverUrl, setCoverUrl] = useState<string | null>(null);

  const applyPlaybackSettings = useCallback((audio: HTMLAudioElement) => {
    const effectiveRate = speed * Math.pow(2, pitchSemitones / 12);
    const preservePitch = pitchSemitones === 0 ? pitchCorrection : false;
    const media = audio as HTMLAudioElement & {
      preservesPitch?: boolean;
      mozPreservesPitch?: boolean;
      webkitPreservesPitch?: boolean;
    };

    audio.playbackRate = effectiveRate;
    media.preservesPitch = preservePitch;
    media.mozPreservesPitch = preservePitch;
    media.webkitPreservesPitch = preservePitch;
  }, [pitchCorrection, pitchSemitones, speed]);

  // Load audio metadata
  useEffect(() => {
    fetch(`${API}/${audioId}`)
      .then((r) => r.json())
      .then((data) => {
        setTitle(data.title ?? "Unknown");
        setCoverUrl(data.coverImagePath ?? null);
      })
      .catch(() => {});
  }, [audioId]);

  // Set up audio element
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    audio.src = `${API}/${audioId}/stream`;
    audio.volume = volume;
    applyPlaybackSettings(audio);
    audio.play().then(() => setIsPlaying(true)).catch(() => {});

    return () => {
      audio.pause();
      audio.src = "";
    };
  }, [applyPlaybackSettings, audioId]);

  // Sync volume
  useEffect(() => {
    if (audioRef.current) {
      audioRef.current.volume = muted ? 0 : volume;
    }
  }, [volume, muted]);

  // Sync speed
  useEffect(() => {
    if (audioRef.current) applyPlaybackSettings(audioRef.current);
  }, [applyPlaybackSettings]);

  const togglePlay = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) { audio.play(); setIsPlaying(true); }
    else { audio.pause(); setIsPlaying(false); }
  }, []);

  const seek = useCallback((e: React.MouseEvent) => {
    const bar = progressRef.current;
    const audio = audioRef.current;
    if (!bar || !audio || !duration) return;
    const rect = bar.getBoundingClientRect();
    const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    audio.currentTime = pct * duration;
  }, [duration]);

  const skip = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (audio) audio.currentTime = Math.max(0, Math.min(audio.duration || 0, audio.currentTime + seconds));
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
      switch (e.key) {
        case " ": e.preventDefault(); togglePlay(); break;
        case "ArrowLeft": skip(-5); break;
        case "ArrowRight": skip(5); break;
        case "m": setMuted((v) => !v); break;
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [togglePlay, skip]);

  const progress = duration > 0 ? (currentTime / duration) * 100 : 0;

  return (
    <div className={`bg-card/95 backdrop-blur-lg border-t border-border shadow-2xl ${compact ? "" : "rounded-t-xl"}`}>
      <audio
        ref={audioRef}
        onTimeUpdate={(e) => setCurrentTime(e.currentTarget.currentTime)}
        onDurationChange={(e) => setDuration(e.currentTarget.duration)}
        onEnded={() => { setIsPlaying(false); onNext?.(); }}
      />

      {/* Progress bar (top edge) */}
      <div
        ref={progressRef}
        onClick={seek}
        className="h-1 bg-surface cursor-pointer group relative"
      >
        <div className="absolute inset-y-0 left-0 bg-accent transition-all" style={{ width: `${progress}%` }} />
        <div
          className="absolute top-1/2 -translate-y-1/2 w-3 h-3 rounded-full bg-accent shadow-md opacity-0 group-hover:opacity-100 transition-opacity"
          style={{ left: `${progress}%`, marginLeft: "-6px" }}
        />
      </div>

      <div className="flex items-center gap-3 px-3 py-2">
        {/* Cover + title */}
        <div className="flex items-center gap-3 flex-1 min-w-0">
          <div className="w-10 h-10 rounded overflow-hidden bg-surface flex-shrink-0 flex items-center justify-center">
            {coverUrl ? (
              <img src={coverUrl} alt="" className="w-full h-full object-cover" />
            ) : (
              <Music className="w-5 h-5 text-muted/30" />
            )}
          </div>
          <div className="min-w-0">
            <p className="text-sm font-medium text-foreground truncate">{title}</p>
            <p className="text-[11px] text-muted tabular-nums">
              {formatTime(currentTime)} / {formatTime(duration)}
            </p>
          </div>
        </div>

        {/* Controls */}
        <div className="flex items-center gap-1">
          {/* Skip back */}
          <button onClick={() => skip(-10)} className="p-1.5 rounded text-secondary hover:text-foreground hover:bg-surface" title="Back 10s">
            <RotateCcw className="w-4 h-4" />
          </button>

          {/* Previous */}
          {onPrevious && (
            <button onClick={onPrevious} className="p-1.5 rounded text-secondary hover:text-foreground hover:bg-surface">
              <SkipBack className="w-4 h-4" />
            </button>
          )}

          {/* Play / Pause */}
          <button
            onClick={togglePlay}
            className="p-2 rounded-full bg-accent text-white hover:bg-accent/80 mx-1"
          >
            {isPlaying ? <Pause className="w-5 h-5" fill="white" /> : <Play className="w-5 h-5 ml-0.5" fill="white" />}
          </button>

          {/* Next */}
          {onNext && (
            <button onClick={onNext} className="p-1.5 rounded text-secondary hover:text-foreground hover:bg-surface">
              <SkipForward className="w-4 h-4" />
            </button>
          )}

          {/* Skip forward */}
          <button onClick={() => skip(10)} className="p-1.5 rounded text-secondary hover:text-foreground hover:bg-surface" title="Forward 10s">
            <RotateCw className="w-4 h-4" />
          </button>
        </div>

        {/* Right side controls */}
        <div className="flex items-center gap-1 flex-1 justify-end">
          {/* Speed */}
          <div className="relative">
            <button
              onClick={() => setShowSpeed((v) => !v)}
              className={`px-1.5 py-1 rounded text-[11px] font-medium ${
                speed !== 1 ? "text-accent bg-accent/10" : "text-secondary hover:text-foreground hover:bg-surface"
              }`}
              title="Playback speed"
            >
              {speed}x
            </button>
            {showSpeed && (
              <div className="absolute bottom-full right-0 mb-1 bg-card border border-border rounded-lg shadow-xl p-1 min-w-[64px]">
                {SPEED_OPTIONS.map((s) => (
                  <button
                    key={s}
                    onClick={() => { setSpeed(s); setShowSpeed(false); }}
                    className={`block w-full text-left px-2 py-1 text-xs rounded ${
                      speed === s ? "bg-accent/10 text-accent" : "text-foreground hover:bg-surface"
                    }`}
                  >
                    {s}x
                  </button>
                ))}
              </div>
            )}
          </div>

          <div className="relative">
            <button
              onClick={() => setShowFilters((value) => !value)}
              className={`p-1.5 rounded ${showFilters || pitchSemitones !== 0 || !pitchCorrection ? "bg-accent/10 text-accent" : "text-secondary hover:text-foreground hover:bg-surface"}`}
              title="Player filters"
            >
              <SlidersHorizontal className="w-4 h-4" />
            </button>
            {showFilters && (
              <div className="absolute bottom-full right-0 mb-1 w-56 rounded-lg border border-border bg-card p-3 shadow-xl">
                <div className="mb-3 flex items-center justify-between">
                  <span className="text-xs font-semibold text-foreground">Filters</span>
                  <button
                    onClick={() => {
                      setPitchCorrection(true);
                      setPitchSemitones(0);
                    }}
                    className="text-[10px] text-accent hover:underline"
                  >
                    Reset
                  </button>
                </div>
                <label className="mb-3 flex items-center justify-between gap-3 text-xs text-secondary">
                  <span>Pitch-correct speed</span>
                  <input
                    type="checkbox"
                    checked={pitchCorrection}
                    onChange={(event) => setPitchCorrection(event.target.checked)}
                    className="rounded border-border bg-surface accent-accent"
                  />
                </label>
                <div className="space-y-2">
                  <div className="flex items-center justify-between text-xs text-secondary">
                    <span>Pitch</span>
                    <button
                      onClick={() => setPitchSemitones(0)}
                      className="text-foreground hover:text-accent"
                      title="Reset pitch"
                    >
                      {pitchSemitones > 0 ? `+${pitchSemitones}` : pitchSemitones} st
                    </button>
                  </div>
                  <input
                    type="range"
                    min={-6}
                    max={6}
                    step={1}
                    value={pitchSemitones}
                    onChange={(event) => {
                      const nextValue = Number(event.target.value);
                      setPitchSemitones(nextValue);
                      if (nextValue !== 0) {
                        setPitchCorrection(false);
                      }
                    }}
                    className="h-1 w-full cursor-pointer accent-accent"
                  />
                  <p className="text-[10px] leading-4 text-muted">
                    Pitch shifts are applied with playback rate, so non-zero pitch also changes tempo.
                  </p>
                </div>
              </div>
            )}
          </div>

          {/* Volume */}
          <button onClick={() => setMuted((v) => !v)} className="p-1.5 rounded text-secondary hover:text-foreground hover:bg-surface">
            {muted || volume === 0 ? <VolumeX className="w-4 h-4" /> : <Volume2 className="w-4 h-4" />}
          </button>
          <input
            type="range"
            min={0}
            max={1}
            step={0.01}
            value={muted ? 0 : volume}
            onChange={(e) => { setVolume(Number(e.target.value)); setMuted(false); }}
            className="w-20 h-1 accent-accent cursor-pointer hidden sm:block"
          />

          {/* Close */}
          <button onClick={onClose} className="p-1.5 rounded text-secondary hover:text-red-400 hover:bg-surface ml-1">
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
