import { AudiosEntityTab } from "./AudiosEntityTab";

export function AudiosTagTab({ entityId, onNavigate }: { entityId: number; onNavigate: (r: any) => void }) {
  return <AudiosEntityTab entityId={entityId} filterParam="tagId" entityLabel="tag" onNavigate={onNavigate} />;
}