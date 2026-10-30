import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { Category } from './categories.models';

export const CATEGORIES_URL = '/api/categories';

/** The user's categories, loaded once and shared by every screen that names a category. */
@Injectable({ providedIn: 'root' })
export class CategoriesStore {
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);
  private readonly state = signal<Category[]>([]);
  private loading: Promise<void> | null = null;

  readonly all = this.state.asReadonly();
  readonly byId = computed(() => new Map(this.state().map((c) => [c.id, c])));
  /** Top-level categories in taxonomy order, each with its visible subcategories. */
  readonly tree = computed(() => {
    const all = this.state();
    return all
      .filter((c) => c.parentId === null)
      .map((parent) => ({ parent, children: all.filter((c) => c.parentId === parent.id) }));
  });
  /** What the user can pick: everything not hidden. */
  readonly selectable = computed(() => this.state().filter((c) => !c.hidden));

  /** Loads the categories unless they are already loaded or loading. */
  load(): Promise<void> {
    this.loading ??= this.reload();
    return this.loading;
  }

  async reload(): Promise<void> {
    try {
      this.state.set(await firstValueFrom(this.http.get<Category[]>(CATEGORIES_URL)));
    } catch (error) {
      this.loading = null;
      throw error;
    }
  }

  /** Replaces one category after an edit. */
  upsert(category: Category): void {
    this.state.update((all) =>
      all.some((c) => c.id === category.id)
        ? all.map((c) => (c.id === category.id ? category : c))
        : [...all, category],
    );
  }

  /** The display name: the user's own, else the i18n name; a subcategory as "Parent › Child". */
  label(id: string | null | undefined, withParent = false): string {
    const category = id ? this.byId().get(id) : undefined;
    if (!category) {
      return '';
    }
    const name: string =
      category.name ?? this.transloco.translate<string>(`categories.${category.key}`);
    if (!withParent || !category.parentId) {
      return name;
    }
    return `${this.label(category.parentId)} › ${name}`;
  }

  /** A stable chart colour (1–6) per top-level category. */
  colorIndex(id: string | null | undefined): number {
    const category = id ? this.byId().get(id) : undefined;
    if (!category) {
      return 6;
    }
    const top = category.parentId ? (this.byId().get(category.parentId) ?? category) : category;
    return (Math.round(top.sortOrder / 10) % 6) + 1;
  }
}
