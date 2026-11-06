import type { CatalogFilter, FilterRequest, LookupItem } from './report.models';

/**
 * Editor state for one filter row. All value slots live side by side so switching the operator
 * (e.g. between → before) keeps what the user already entered where it still makes sense.
 */
export interface FilterDraft {
  id: number;
  key: string;
  op: string;
  text: string;
  from: Date | null;
  to: Date | null;
  /** Selected options for `in` filters (enum, lookup) and the single pick for lookup `eq`. */
  items: LookupItem[];
}

export const OPERATOR_LABELS: Record<string, string> = {
  eq: 'Is',
  contains: 'Contains',
  startsWith: 'Starts with',
  between: 'Between',
  before: 'Before',
  after: 'After',
  in: 'Is any of',
};

let nextId = 1;

export function newDraft(filter: CatalogFilter): FilterDraft {
  return { id: nextId++, key: filter.key, op: filter.operators[0], text: '', from: null, to: null, items: [] };
}

/** Calendar day as `yyyy-MM-dd` in local time (dates are Manila calendar days on the server). */
export function toIsoDay(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The API filter for a row, or `null` while the row is incomplete. */
export function toRequest(draft: FilterDraft, filter: CatalogFilter): FilterRequest | null {
  const value = requestValue(draft, filter);
  return value === null ? null : { entity: filter.entity, field: filter.field, op: draft.op, value };
}

function requestValue(draft: FilterDraft, filter: CatalogFilter): unknown {
  switch (draft.op) {
    case 'between':
      return draft.from && draft.to ? [toIsoDay(draft.from), toIsoDay(draft.to)] : null;
    case 'before':
    case 'after':
      return draft.from ? toIsoDay(draft.from) : null;
    case 'in':
      return draft.items.length ? draft.items.map((i) => i.value) : null;
    default:
      if (filter.lookup) {
        return draft.items[0]?.value ?? null;
      }
      return draft.text.trim() ? draft.text.trim() : null;
  }
}

/** Ids of every program picked in any program filter: the scope of the outlet lookup. */
export function selectedProgramIds(drafts: readonly FilterDraft[], dependsOnKey: string): number[] {
  const ids = drafts
    .filter((d) => d.key === dependsOnKey)
    .flatMap((d) => d.items.map((i) => Number(i.value)));
  return [...new Set(ids)];
}

/** Drops outlet picks whose program is no longer selected. Pure; returns the same array when nothing changes. */
export function pruneDependents(drafts: FilterDraft[], filters: readonly CatalogFilter[]): FilterDraft[] {
  let changed = false;
  const result = drafts.map((draft) => {
    const dependsOn = filters.find((f) => f.key === draft.key)?.dependsOn;
    if (!dependsOn) {
      return draft;
    }
    const allowed = new Set(selectedProgramIds(drafts, dependsOn));
    const items = draft.items.filter((i) => i.parentId !== null && allowed.has(i.parentId));
    if (items.length === draft.items.length) {
      return draft;
    }
    changed = true;
    return { ...draft, items };
  });
  return changed ? result : drafts;
}
