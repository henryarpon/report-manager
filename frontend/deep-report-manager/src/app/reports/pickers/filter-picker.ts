import { CdkTrapFocus } from '@angular/cdk/a11y';
import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { Component, computed, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CatalogFilter } from '../report.models';
import { POPOVER_POSITIONS } from './column-picker';

export interface FilterGroup {
  entity: string;
  filters: CatalogFilter[];
}

const TYPE_ICONS: Record<string, string> = {
  string: 'text_fields',
  date: 'calendar_today',
  enum: 'checklist',
  lookup: 'person_search',
  number: 'tag',
};

/**
 * "Add filter" button with a searchable popover of every catalog filter, grouped by entity.
 * Each group explains how it relates to the report ("Has a transaction matching…").
 * Arrow keys move between options; picking one closes the popover.
 */
@Component({
  selector: 'app-filter-picker',
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, CdkTrapFocus, MatButtonModule, MatIconModule],
  template: `
    <button
      mat-stroked-button
      type="button"
      class="picker-trigger add-filter"
      cdkOverlayOrigin
      #origin="cdkOverlayOrigin"
      #trigger
      aria-haspopup="dialog"
      [attr.aria-expanded]="open()"
      (click)="open.set(!open())"
    >
      <mat-icon aria-hidden="true">add</mat-icon>
      Add filter
    </button>

    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="origin"
      [cdkConnectedOverlayOpen]="open()"
      [cdkConnectedOverlayPositions]="positions"
      cdkConnectedOverlayHasBackdrop
      cdkConnectedOverlayFlexibleDimensions
      [cdkConnectedOverlayViewportMargin]="12"
      cdkConnectedOverlayBackdropClass="cdk-overlay-transparent-backdrop"
      (backdropClick)="close()"
      (overlayKeydown)="$event.key === 'Escape' && close()"
      (detach)="close()"
    >
      <div
        class="popover"
        role="dialog"
        aria-label="Add a filter"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
        (keydown)="onKeydown($event)"
      >
        <div class="popover-search">
          <mat-icon aria-hidden="true">search</mat-icon>
          <input
            #searchBox
            type="search"
            placeholder="Search filters"
            aria-label="Search filters"
            [value]="search()"
            (input)="search.set(searchBox.value)"
          />
        </div>
        <div class="popover-body" #list>
          @for (group of visibleGroups(); track group.entity) {
            <div class="popover-group" role="group" [attr.aria-label]="group.entity">
              <p class="group-title">
                {{ group.entity }}
                @if (scopes()[group.entity]; as scope) {
                  <span class="group-scope">{{ scope }}</span>
                }
              </p>
              @for (filter of group.filters; track filter.key) {
                <button type="button" class="pick-option" (click)="pick(filter)">
                  <mat-icon aria-hidden="true">{{ icon(filter) }}</mat-icon>
                  {{ filter.label }}
                </button>
              }
            </div>
          } @empty {
            <p class="muted popover-empty">No filter matches “{{ search() }}”.</p>
          }
        </div>
      </div>
    </ng-template>
  `,
})
export class FilterPicker {
  readonly groups = input.required<FilterGroup[]>();
  /** How each entity's filters relate to the current root. */
  readonly scopes = input<Record<string, string>>({});
  readonly picked = output<CatalogFilter>();

  protected readonly open = signal(false);
  protected readonly search = signal('');
  protected readonly positions = POPOVER_POSITIONS;

  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly list = viewChild<ElementRef<HTMLElement>>('list');

  protected readonly visibleGroups = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.groups()
      .map((g) => ({ ...g, filters: g.filters.filter((f) => !term || f.label.toLowerCase().includes(term)) }))
      .filter((g) => g.filters.length);
  });

  focus(): void {
    this.trigger().nativeElement.focus();
  }

  protected icon(filter: CatalogFilter): string {
    return TYPE_ICONS[filter.lookup ? 'lookup' : filter.type] ?? 'filter_alt';
  }

  protected pick(filter: CatalogFilter): void {
    this.close();
    this.picked.emit(filter);
  }

  protected close(): void {
    this.open.set(false);
    this.search.set('');
  }

  /** Up/down arrows walk the option list; Enter in the search box picks the first match. */
  protected onKeydown(event: KeyboardEvent): void {
    const options = [...(this.list()?.nativeElement.querySelectorAll<HTMLButtonElement>('.pick-option') ?? [])];
    if (!options.length) {
      return;
    }
    const target = event.target as HTMLElement;
    const index = options.indexOf(target as HTMLButtonElement);
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      options[Math.min(index + 1, options.length - 1)].focus();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      if (index > 0) {
        options[index - 1].focus();
      }
    } else if (event.key === 'Enter' && target.tagName === 'INPUT') {
      event.preventDefault();
      options[0].click();
    }
  }
}
