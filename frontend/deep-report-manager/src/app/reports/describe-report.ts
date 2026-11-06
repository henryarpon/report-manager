import type { FilterDraft } from './filter-draft';
import type { CatalogFilter } from './report.models';

const dayFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });

function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1);
}

/** "A", "A or B", "A, B or 3 others". */
function anyOf(labels: string[]): string {
  if (labels.length <= 2) {
    return labels.join(' or ');
  }
  return `${labels.slice(0, 2).join(', ')} or ${labels.length - 2} others`;
}

/** One complete filter as a plain-English clause, e.g. "transaction date is between 1 Jan 2026 and 31 Mar 2026". */
export function describeFilter(draft: FilterDraft, filter: CatalogFilter): string {
  const field = lowerFirst(filter.label);
  switch (draft.op) {
    case 'between':
      return `${field} is between ${dayFormat.format(draft.from!)} and ${dayFormat.format(draft.to!)}`;
    case 'before':
      return `${field} is before ${dayFormat.format(draft.from!)}`;
    case 'after':
      return `${field} is after ${dayFormat.format(draft.from!)}`;
    case 'in':
      return `${field} is ${anyOf(draft.items.map((i) => i.label))}`;
    case 'contains':
      return `${field} contains “${draft.text.trim()}”`;
    case 'startsWith':
      return `${field} starts with “${draft.text.trim()}”`;
    default:
      return filter.lookup
        ? `${field} is ${draft.items[0]?.label ?? ''}`
        : `${field} is “${draft.text.trim()}”`;
  }
}

/** "1,501 transactions" / "1 transaction". `rootLabel` is the plural catalog label ("Transactions"). */
export function countNoun(count: number, rootLabel: string): string {
  const plural = rootLabel.toLowerCase();
  const noun = count === 1 && plural.endsWith('s') ? plural.slice(0, -1) : plural;
  return `${count.toLocaleString()} ${noun}`;
}
