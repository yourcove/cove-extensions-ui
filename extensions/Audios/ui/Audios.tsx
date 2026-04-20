/**
 * Audios Extension — Entry point
 * Exports all components used by the extension manifest.
 */

import { AudiosPage } from "./AudiosPage";
import { AudioDetailPage } from "./AudioDetailPage";
import { AudioPlayer } from "./AudioPlayer";
import { AudioSettingsPanel } from "./AudioSettingsPanel";
import { AudioExtensionsSettings } from "./AudioExtensionsSettings";
import { GroupAudiosCardFooter, StudioAudiosCardFooter, TagAudiosCardFooter } from "./AudioCardFooters";
import { AudiosPerformerTab } from "./AudiosPerformerTab";
import { AudiosGroupTab } from "./AudiosGroupTab";
import { AudiosTagTab } from "./AudiosTagTab";
import { AudiosStudioTab } from "./AudiosStudioTab";

export default {
  components: {
    AudiosPage,
    AudioDetailPage,
    AudioPlayer,
    AudioSettingsPanel,
    AudioExtensionsSettings,
    GroupAudiosCardFooter,
    StudioAudiosCardFooter,
    TagAudiosCardFooter,
    AudiosPerformerTab,
    AudiosGroupTab,
    AudiosTagTab,
    AudiosStudioTab,
  },
};
