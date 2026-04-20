import { AudiosEntityTab } from "./AudiosEntityTab";

export function AudiosPerformerTab({ entityId, onNavigate }: { entityId: number; onNavigate: (r: any) => void }) {
  return <AudiosEntityTab entityId={entityId} filterParam="performerId" entityLabel="performer" onNavigate={onNavigate} />;
}