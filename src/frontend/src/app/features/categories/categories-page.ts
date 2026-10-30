import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, LOCALE_ID, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Category } from '../../core/categories/categories.models';
import { CATEGORIES_URL, CategoriesStore } from '../../core/categories/categories.store';
import { Snackbar } from '../../shared/ui/snackbar';
import { pluralCategory } from '../import/plural';
import { RULES_URL, TransactionsApi } from '../transactions/transactions.api';
import { Rule } from '../transactions/transactions.models';

export const MAX_NAME_LENGTH = 60;

type Editing = { kind: 'rename'; id: string } | { kind: 'add'; parentId: string } | null;

/**
 * Category management: rename, hide and add subcategories. The top-level categories keep their
 * meaning. Below, the user's rules from "apply to all", which can be removed.
 */
@Component({
  selector: 'app-categories-page',
  imports: [TranslocoDirective, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './categories-page.html',
  styleUrl: './categories-page.scss',
})
export class CategoriesPage {
  private readonly http = inject(HttpClient);
  private readonly api = inject(TransactionsApi);
  private readonly snackbar = inject(Snackbar);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  protected readonly categories = inject(CategoriesStore);

  protected readonly maxLength = MAX_NAME_LENGTH;
  protected readonly editing = signal<Editing>(null);
  protected readonly saving = signal(false);
  protected readonly failed = signal(false);
  protected readonly rules = httpResource<Rule[]>(() => RULES_URL, { defaultValue: [] });

  constructor() {
    this.categories.load().catch(() => this.failed.set(true));
  }

  protected isRenaming(id: string): boolean {
    const e = this.editing();
    return e?.kind === 'rename' && e.id === id;
  }

  protected isAdding(parentId: string): boolean {
    const e = this.editing();
    return e?.kind === 'add' && e.parentId === parentId;
  }

  /** An empty name brings back the default name of a global category. */
  protected rename(category: Category, value: string): void {
    const name = value.trim();
    if (!name && category.own) {
      return;
    }
    this.save(category, name || null, category.hidden);
  }

  protected toggleHidden(category: Category): void {
    this.save(category, category.name, !category.hidden);
  }

  protected add(parentId: string, value: string): void {
    const name = value.trim();
    if (!name) {
      return;
    }
    this.saving.set(true);
    this.http.post<Category>(CATEGORIES_URL, { parentId, name }).subscribe({
      next: (created) => this.saved(created),
      error: () => this.fail(),
    });
  }

  protected ruleText(rule: Rule): string {
    return rule.label ?? rule.pattern ?? '';
  }

  protected deleteRule(rule: Rule): void {
    this.api.deleteRule(rule.id).subscribe({
      next: ({ changed }) => {
        const form = pluralCategory(changed, this.locale);
        this.snackbar.show({
          text: this.transloco.translate(`categoriesPage.ruleDeleted.${form}`, { count: changed }),
        });
        this.rules.reload();
      },
      error: () => this.fail(),
    });
  }

  private save(category: Category, name: string | null, hidden: boolean): void {
    this.saving.set(true);
    this.http.put<Category>(`${CATEGORIES_URL}/${category.id}`, { name, hidden }).subscribe({
      next: (updated) => this.saved(updated),
      error: () => this.fail(),
    });
  }

  private saved(category: Category): void {
    this.categories.upsert(category);
    this.saving.set(false);
    this.failed.set(false);
    this.editing.set(null);
  }

  private fail(): void {
    this.saving.set(false);
    this.failed.set(true);
  }
}
