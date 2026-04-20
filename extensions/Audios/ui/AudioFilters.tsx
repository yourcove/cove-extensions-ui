import type { CriterionDefinition } from "@cove/runtime/components";

type NumberModifier = "EQUALS" | "NOT_EQUALS" | "GREATER_THAN" | "LESS_THAN" | "BETWEEN" | "NOT_BETWEEN" | "IS_NULL" | "NOT_NULL";
type StringModifier = "EQUALS" | "NOT_EQUALS" | "INCLUDES" | "EXCLUDES" | "MATCHES_REGEX" | "NOT_MATCHES_REGEX" | "IS_NULL" | "NOT_NULL";
type MultiIdModifier = "INCLUDES" | "INCLUDES_ALL" | "EXCLUDES" | "EXCLUDES_ALL";

interface AudioNumberCriterion {
  modifier?: NumberModifier;
  value?: number;
  value2?: number;
}

interface AudioStringCriterion {
  modifier?: StringModifier;
  value?: string;
}

interface AudioBoolCriterion {
  modifier?: "EQUALS";
  value?: boolean;
}

interface AudioMultiIdCriterion {
  modifier?: MultiIdModifier;
  value?: number[];
  excludes?: number[];
  _names?: Record<string, string>;
}

export interface AudioObjectFilter {
  tags?: AudioMultiIdCriterion;
  performers?: AudioMultiIdCriterion;
  groups?: AudioMultiIdCriterion;
  studios?: AudioMultiIdCriterion;
  genre?: AudioStringCriterion;
  organized?: AudioBoolCriterion;
  rating?: AudioNumberCriterion;
}

export type AudioFilterField = keyof AudioObjectFilter;

export const AUDIO_CRITERIA: CriterionDefinition[] = [
  { id: "tags", label: "Tags", type: "multiId", entityType: "tags", filterKey: "tags" },
  { id: "performers", label: "Performers", type: "multiId", entityType: "performers", filterKey: "performers" },
  { id: "groups", label: "Groups", type: "multiId", entityType: "groups", filterKey: "groups" },
  { id: "studios", label: "Studios", type: "multiId", entityType: "studios", filterKey: "studios" },
  { id: "genre", label: "Genre", type: "string", filterKey: "genre" },
  { id: "organized", label: "Organized", type: "bool", filterKey: "organized" },
  { id: "rating", label: "Rating", type: "rating", filterKey: "rating" },
];

function hasCriterionValue(value: unknown): boolean {
  if (value == null) return false;
  if (typeof value !== "object") return value !== "";

  const criterion = value as {
    modifier?: string;
    value?: unknown;
    value2?: unknown;
    excludes?: unknown[];
  };

  if (criterion.modifier === "IS_NULL" || criterion.modifier === "NOT_NULL") {
    return true;
  }

  if (Array.isArray(criterion.value)) {
    return criterion.value.length > 0 || (criterion.excludes?.length ?? 0) > 0;
  }

  if (typeof criterion.value === "boolean") {
    return true;
  }

  if (criterion.value != null && criterion.value !== "") {
    return true;
  }

  if ((criterion.excludes?.length ?? 0) > 0) {
    return true;
  }

  return criterion.value2 != null && criterion.value2 !== "";
}

export function omitHiddenAudioFilters(filters: AudioObjectFilter, hiddenFields: AudioFilterField[] = []): AudioObjectFilter {
  const next = Object.fromEntries(
    Object.entries(filters).filter(([key, value]) => !hiddenFields.includes(key as AudioFilterField) && hasCriterionValue(value)),
  );

  return next as AudioObjectFilter;
}

export function appendAudioFilters(params: URLSearchParams, filters: AudioObjectFilter, hiddenFields: AudioFilterField[] = []) {
  const visibleFilters = omitHiddenAudioFilters(filters, hiddenFields);
  if (Object.keys(visibleFilters).length > 0) {
    params.set("filters", JSON.stringify(visibleFilters));
  }
}

export function filterAudioCriteria(hiddenFields: AudioFilterField[] = []) {
  return AUDIO_CRITERIA.filter((criterion) => !hiddenFields.includes(criterion.id as AudioFilterField));
}

export function countActiveAudioFilters(filters: AudioObjectFilter, hiddenFields: AudioFilterField[] = []) {
  return Object.keys(omitHiddenAudioFilters(filters, hiddenFields)).length;
}
