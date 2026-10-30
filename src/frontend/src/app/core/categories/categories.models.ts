export type CategoryKind = 'spending' | 'income' | 'refund' | 'excluded';

/** A category as the API returns it: global (named by `key` through i18n) or the user's own. */
export interface Category {
  id: string;
  /** i18n key under `categories.*` of a global category; `null` for the user's own. */
  key: string | null;
  /** The user's name for it; overrides the i18n name of a global category. */
  name: string | null;
  parentId: string | null;
  kind: CategoryKind;
  sortOrder: number;
  hidden: boolean;
  own: boolean;
}
