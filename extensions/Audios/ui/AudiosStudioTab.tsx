import { AudiosEntityTab } from "./AudiosEntityTab";

export function AudiosStudioTab({ entityId, onNavigate }: { entityId: number; onNavigate: (r: any) => void }) {
  return <AudiosEntityTab entityId={entityId} filterParam="studioId" entityLabel="studio" onNavigate={onNavigate} />;
}
