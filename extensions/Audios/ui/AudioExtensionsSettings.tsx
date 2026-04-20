import { useState, useEffect } from "react";

const API_BASE = "/api/ext/audios";

export function AudioExtensionsSettings() {
  const [extensions, setExtensions] = useState<string[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    fetch(`${API_BASE}/settings`)
      .then((r) => r.json())
      .then((data) => {
        setExtensions(data.audioExtensions ?? []);
        setLoaded(true);
      })
      .catch(() => setLoaded(true));
  }, []);

  const handleChange = (value: string) => {
    const list = value
      .split("\n")
      .map((s) => s.trim())
      .filter(Boolean);
    setExtensions(list);
  };

  const handleBlur = async () => {
    // Read full settings first to avoid clobbering other fields
    try {
      const res = await fetch(`${API_BASE}/settings`);
      const current = await res.json();
      await fetch(`${API_BASE}/settings`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...current, audioExtensions: extensions }),
      });
    } catch {
      // silently fail — main settings save will persist
    }
  };

  if (!loaded) return null;

  return (
    <div>
      <textarea
        value={extensions.join("\n")}
        onChange={(e) => handleChange(e.target.value)}
        onBlur={handleBlur}
        rows={7}
        className="w-full px-3 py-2 text-sm bg-card border border-border rounded-lg focus:outline-none focus:border-accent font-mono"
        placeholder={".mp3\n.flac\n.ogg\n.wav"}
      />
      <p className="mt-1 text-xs text-muted">One extension per line. Used by the Audios extension when scanning for audio files.</p>
    </div>
  );
}
