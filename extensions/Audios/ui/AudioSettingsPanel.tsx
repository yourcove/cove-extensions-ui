import { useState, useEffect } from "react";

const API_BASE = "/api/ext/audios";

interface AudioSettings {
  scanPaths: string[];
  audioExtensions: string[];
  extractCoverArt: boolean;
}

export function AudioSettingsPanel() {
  const [settings, setSettings] = useState<AudioSettings>({
    scanPaths: [],
    audioExtensions: [".mp3", ".flac", ".ogg", ".wav", ".m4a", ".aac", ".wma", ".opus"],
    extractCoverArt: true,
  });
  const [newPath, setNewPath] = useState("");
  const [saving, setSaving] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    fetch(`${API_BASE}/settings`)
      .then(r => r.json())
      .then(setSettings)
      .catch(() => {});
  }, []);

  const addPath = () => {
    const trimmed = newPath.trim();
    if (!trimmed || settings.scanPaths.includes(trimmed)) return;
    setSettings(s => ({ ...s, scanPaths: [...s.scanPaths, trimmed] }));
    setNewPath("");
  };

  const removePath = (path: string) => {
    setSettings(s => ({ ...s, scanPaths: s.scanPaths.filter(p => p !== path) }));
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      await fetch(`${API_BASE}/settings`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(settings),
      });
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    } finally {
      setSaving(false);
    }
  };

  const handleScan = async () => {
    setScanning(true);
    try {
      await fetch(`${API_BASE}/scan`, { method: "POST" });
    } finally {
      setScanning(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Scan paths */}
      <div>
        <h3 className="text-sm font-semibold text-foreground mb-2">Scan Paths</h3>
        <p className="text-xs text-muted mb-3">Directories to scan for audio files.</p>

        <div className="space-y-2 mb-3">
          {settings.scanPaths.length === 0 ? (
            <p className="text-xs text-muted italic">No paths configured</p>
          ) : (
            settings.scanPaths.map(path => (
              <div key={path} className="flex items-center gap-2 bg-surface rounded px-3 py-1.5">
                <span className="text-sm text-secondary flex-1 font-mono truncate">{path}</span>
                <button
                  onClick={() => removePath(path)}
                  className="text-xs text-muted hover:text-red-400"
                >
                  ✕
                </button>
              </div>
            ))
          )}
        </div>

        <div className="flex gap-2">
          <input
            type="text"
            value={newPath}
            onChange={e => setNewPath(e.target.value)}
            placeholder="/path/to/audio/files"
            className="flex-1 px-3 py-1.5 text-sm bg-card border border-border rounded focus:outline-none focus:border-accent"
            onKeyDown={e => { if (e.key === "Enter") addPath(); }}
          />
          <button
            onClick={addPath}
            className="px-3 py-1.5 text-sm bg-accent text-white rounded hover:bg-accent-hover"
          >
            Add
          </button>
        </div>
      </div>

      {/* File extensions */}
      <div>
        <h3 className="text-sm font-semibold text-foreground mb-2">Audio Extensions</h3>
        <p className="text-xs text-muted mb-3">File extensions to include when scanning.</p>
        <div className="flex flex-wrap gap-1.5">
          {settings.audioExtensions.map(ext => (
            <span key={ext} className="px-2 py-0.5 bg-surface rounded text-xs text-secondary">{ext}</span>
          ))}
        </div>
      </div>

      {/* Options */}
      <div>
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={settings.extractCoverArt}
            onChange={e => setSettings(s => ({ ...s, extractCoverArt: e.target.checked }))}
            className="rounded accent-accent"
          />
          <span className="text-sm text-foreground">Extract embedded cover art during scan</span>
        </label>
      </div>

      {/* Actions */}
      <div className="flex gap-3 pt-2 border-t border-border">
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-4 py-2 text-sm bg-accent text-white rounded hover:bg-accent-hover disabled:opacity-50"
        >
          {saving ? "Saving..." : saved ? "✓ Saved" : "Save Settings"}
        </button>
        <button
          onClick={handleScan}
          disabled={scanning}
          className="px-4 py-2 text-sm bg-card border border-border text-secondary rounded hover:text-foreground hover:border-accent disabled:opacity-50"
        >
          {scanning ? "Starting..." : "Run Scan Now"}
        </button>
      </div>
    </div>
  );
}
