import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CategoriesStore } from '../../core/categories/categories.store';

/**
 * The likely categories as large chips (one tap picks), plus "Więcej…" for the full list grouped
 * by top-level category. Used by the transaction sheet, the review queue and onboarding.
 */
@Component({
  selector: 'app-category-picker',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="picker" *transloco="let t">
      <div class="picker__chips" role="group" [attr.aria-label]="t('picker.suggestions')">
        @for (id of chips(); track id) {
          <button
            type="button"
            class="chip"
            [class.chip--current]="id === current()"
            [attr.aria-pressed]="id === current()"
            [disabled]="disabled()"
            (click)="picked.emit(id)"
          >
            {{ categories.label(id, true) }}
          </button>
        }
        <button
          type="button"
          class="chip chip--more"
          [attr.aria-expanded]="expanded()"
          [disabled]="disabled()"
          (click)="expanded.set(!expanded())"
        >
          {{ t(expanded() ? 'picker.less' : 'picker.more') }}
        </button>
      </div>

      @if (expanded()) {
        <div class="picker__all">
          @for (group of groups(); track group.parent.id) {
            <div class="picker__group">
              <button
                type="button"
                class="option"
                [class.option--current]="group.parent.id === current()"
                [disabled]="disabled()"
                (click)="picked.emit(group.parent.id)"
              >
                {{ categories.label(group.parent.id) }}
              </button>
              @for (child of group.children; track child.id) {
                <button
                  type="button"
                  class="option option--child"
                  [class.option--current]="child.id === current()"
                  [disabled]="disabled()"
                  (click)="picked.emit(child.id)"
                >
                  {{ categories.label(child.id) }}
                </button>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .picker {
      display: grid;
      gap: var(--space-3);
    }
    .picker__chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .chip {
      min-height: var(--touch-target);
      padding: 0 var(--space-4);
      border: var(--border);
      border-radius: var(--radius-pill);
      background: var(--color-surface-raised);
      color: var(--color-ink);
      font: inherit;
      font-size: var(--text-support);
      font-weight: var(--weight-medium);
      cursor: pointer;
      transition:
        background-color var(--duration-fast) var(--ease),
        border-color var(--duration-fast) var(--ease);
      &:hover:not(:disabled) {
        border-color: var(--color-ink-subtle);
      }
      &:disabled {
        opacity: 0.5;
        cursor: progress;
      }
    }
    .chip--current {
      border-color: var(--color-accent);
      background: var(--color-accent-soft);
    }
    .chip--more {
      background: transparent;
      color: var(--color-accent-text);
    }
    .picker__all {
      display: grid;
      gap: var(--space-2);
      max-height: 45dvh;
      overflow-y: auto;
      overscroll-behavior: contain;
      border-top: var(--border);
      padding-top: var(--space-2);
    }
    .picker__group {
      display: grid;
    }
    .option {
      min-height: var(--touch-target);
      padding: 0 var(--space-2);
      border: 0;
      border-radius: var(--radius-control);
      background: transparent;
      color: var(--color-ink);
      font: inherit;
      text-align: left;
      cursor: pointer;
      &:hover:not(:disabled) {
        background: var(--color-surface-raised);
      }
    }
    .option--child {
      padding-left: var(--space-6);
      color: var(--color-ink-muted);
    }
    .option--current {
      background: var(--color-accent-soft);
      color: var(--color-ink);
      font-weight: var(--weight-medium);
    }
  `,
})
export class CategoryPicker {
  protected readonly categories = inject(CategoriesStore);

  /** Likely categories, best first; shown as chips. */
  readonly suggestions = input<readonly string[]>([]);
  /** The category it has now: marked, and kept as the first chip. */
  readonly current = input<string | null>(null);
  readonly disabled = input(false);
  readonly picked = output<string>();

  protected readonly expanded = signal(false);

  protected readonly chips = computed(() => {
    const known = this.categories.byId();
    const current = this.current();
    const ids = [...(current ? [current] : []), ...this.suggestions()];
    return [...new Set(ids)].filter((id) => known.has(id)).slice(0, current ? 4 : 3);
  });

  protected readonly groups = computed(() =>
    this.categories
      .tree()
      .filter((g) => !g.parent.hidden)
      .map((g) => ({ parent: g.parent, children: g.children.filter((c) => !c.hidden) })),
  );
}
