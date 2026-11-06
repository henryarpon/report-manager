# Handoff: front-end structure review (DEEP Reports)

Date: 2026-11-05

## Task

The user asked (in Taglish): "review mo front-end structure", meaning *review the front-end structure*.
The review was **interrupted before it was delivered**. All files were read and a production build was run.
The next agent should **present the review below** (refine it if needed), then ask which items to act on.
Make **no code changes** unless the user asks.

Project rules: `../../CLAUDE.md` (repo root) and `AGENTS.md` (this folder: Angular signals/standalone/WCAG AA rules).
This folder is its own git repo (branch `master`).

## What was checked

- Every file under `src/` (about 3.1k lines in total), plus `angular.json`, `package.json` and `proxy.conf.json` (`/api` → `http://localhost:5016`).
- `npx ng build` (production) **passed with no warnings**: initial 323 kB (88 kB transfer); lazy `reports-page` chunk 638 kB raw / 112 kB transfer.
- No `*.spec.ts` files exist. Vitest is configured, but CLAUDE.md defers automated tests.

## Structure as found

```
src/app/
  app.{ts,html,scss,config.ts,routes.ts}    shell: skip link, app bar, lazy /reports route
  reports/
    reports-page.{ts,html,scss}             container: catalog, draft editor, applied report, run, export (425 + 236 + 301 lines)
    report-table.ts                         presentational mat-table + paginator; formatCell() lives here
    report.models.ts                        API shapes
    reports-api.ts                          @Service client + error-message helpers
    filter-draft.ts / describe-report.ts / download.ts   pure helpers
    report-paginator-intl.ts
    filters/   filter-row.{ts,html}, lookup-typeahead.ts, option-select.ts
    pickers/   column-picker.ts, filter-picker.ts (popovers via CDK overlay)
src/styles.scss + src/styles/_{filters,pickers,report,table}.scss   tokens, Material theme, and most component styles (global)
```

## Review findings (not yet shown to the user)

**Strengths:** one lazy feature folder; a container page with presentational children (signal `input()`/`output()`); pure logic split into helper files; signals, `httpResource`, `linkedSignal`, native control flow and `@Service` all follow AGENTS.md; strong a11y work (live announcer, captions, focus management, AA tokens); the draft-vs-applied split keeps the table and export in sync.

**Issues, roughly by priority:**

1. **`reports-page.ts` is a god component.** It holds catalog, draft editor, applied report, run resource, result caching, export and announcements. Suggested fix: move the state into a page-provided store (e.g. `report-builder.store.ts`). Split the template into `<app-report-builder>` (rail) and `<app-report-results>` (stage).
2. **The frontend hardcodes knowledge the catalog should drive** (CLAUDE.md: "one registry drives the Angular pickers"):
   - `TYPEAHEAD_MIN_CHARS` in `filters/filter-row.ts` (customers 3, programs 1) duplicates a backend rule.
   - `ROOT_DETAILS` icons/descriptions and the `RootKey` union in `reports-page.ts` / `report.models.ts`.
   - `rootEntity()` capitalises the root key, and `columnEntity()` parses `"Entity.Field"` keys. Both rely on naming conventions; the catalog could send `entity` explicitly.
   - The `disabledHint` "Add a Program filter…" text, and `selectedProgramIds` assuming the parent is a program (`Number(value)`).
   - `OPERATOR_LABELS` (lower priority).
3. **Component styles live in global partials** (`src/styles/_filters.scss`, `_table.scss`, part of `_pickers.scss`), away from their components. CDK overlay popovers do need global CSS, but `filter-row` and `report-table` could use their own `styleUrl`. `reports-page.scss` is 4.8 kB, over the 4 kB `anyComponentStyle` warning budget in source. The build did not warn, probably because of minification. Either keep it as a deliberate choice and document it, or colocate.
4. **API ownership is split:** `ReportsApi` exposes `catalogUrl`/`runUrl` strings for `httpResource`, while the page builds the request objects. Suggestion: add factory methods (e.g. `api.catalogResource()`, `api.runResource(request)`).
5. **Fragile hack in `discardChanges()`:** it reads `this.columns()` to flush the `linkedSignal` before `.set`. A store or an explicit source would remove this.
6. **Two different date formatters:** `Intl` en-GB in `describe-report.ts` and a manual `MONTHS` array in `report-table.ts`. Unify them in one `format.ts`, and move `formatCell` out of the component.
7. **Small logic in the template:** `(clearFilters)="clearFilters(); runReport()"`. Move it into a method.
8. **No tests.** The pure helpers (`filter-draft`, `describe-report`, `download`, `formatCell`) would be cheap Vitest targets if testing is un-deferred.

## Suggested skills

- `mattpocock-skills:codebase-design`: for item 1 (store/component seams, deep-module framing).
- `frontend-design`: only if the split touches the visual layout.
- `mattpocock-skills:tdd`: if the user wants tests before refactoring the pure helpers.
- `code-review` / `simplify`: after any refactor is made.

## Notes

- The user prefers to keep sessions under ~120k tokens (see auto-memory).
- The user writes in Taglish; reply in English unless they switch.
