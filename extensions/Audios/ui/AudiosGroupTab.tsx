import { AudiosEntityTab } from "./AudiosEntityTab";

export function AudiosGroupTab({ entityId, onNavigate }: { entityId: number; onNavigate: (r: any) => void }) {
  return <AudiosEntityTab entityId={entityId} filterParam="groupId" entityLabel="group" onNavigate={onNavigate} />;
}