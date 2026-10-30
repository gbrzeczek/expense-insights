export type ClassificationSource = 'unclassified' | 'globalRule' | 'userRule' | 'userCorrection';
export type TransactionFilter = 'all' | 'unclassified' | 'review';

export interface TransactionItem {
  id: string;
  /** `yyyy-MM-dd` */
  date: string;
  status: 'pending' | 'booked' | 'expired';
  amount: number;
  currency: string;
  kind: string;
  /** The shop's domain for payments through a processor, else the counterparty. */
  merchant: string;
  merchantKey: string;
  categoryId: string | null;
  source: ClassificationSource;
  confidence: number;
  /** A guess waiting in "Do sprawdzenia". */
  needsReview: boolean;
  /** Why it got its category: an i18n code under `transactions.reason.*`. */
  reason: string | null;
  reasonValue: string | null;
  offsetsCategoryId: string | null;
}

export interface TransactionPage {
  items: TransactionItem[];
  nextOffset: number | null;
}

/** What "apply to all" would do: a rule for the merchant or for the person (by account). */
export interface ApplyToAllOffer {
  matchType: 'exact' | 'counterpartyAccount';
  label: string;
  count: number;
}

export interface TransactionDetail {
  transaction: TransactionItem;
  title: string;
  counterpartyRaw: string;
  /** Masked, e.g. `•••• 1234`. */
  counterpartyAccount: string | null;
  counterpartyBank: string | null;
  accountName: string;
  bookingDate: string | null;
  purchaseDate: string | null;
  originalAmount: number | null;
  originalCurrency: string | null;
  raw: string[];
  /** Up to three other likely categories, best first. */
  suggestions: string[];
  applyToAll: ApplyToAllOffer | null;
}

export interface CorrectionResponse {
  transaction: TransactionItem;
  applyToAll: ApplyToAllOffer | null;
}

export interface Rule {
  id: string;
  matchType: 'exact' | 'contains' | 'trigram' | 'counterpartyAccount' | 'titleKeyword';
  /** `null` for a rule by account: its pattern is never sent. */
  pattern: string | null;
  label: string | null;
  categoryId: string;
  createdAt: string;
}

export interface ApplyToAllResponse {
  rule: Rule;
  changed: number;
}

export interface MerchantGroup {
  merchantKey: string;
  name: string;
  count: number;
  total: number;
  currency: string;
  /** A guess some of its transactions have: offered first. */
  guess: string | null;
}

export interface SpendingProgress {
  currency: string;
  categorized: number;
  total: number;
  /** Categorized ÷ total by amount, 0–1. */
  share: number;
}

export interface MerchantReview {
  groups: MerchantGroup[];
  progress: SpendingProgress[];
}
