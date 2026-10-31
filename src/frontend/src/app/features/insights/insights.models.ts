import { TransactionItem } from '../transactions/transactions.models';

export type InsightType =
  | 'categoryChange'
  | 'pace'
  | 'largePayment'
  | 'frequentSmall'
  | 'newSubscription'
  | 'subscriptionPriceChange'
  | 'uncategorized';

/** One insight as typed parameters; every number is computed by the backend. */
export interface Insight {
  type: InsightType;
  score: number;
  direction: 'up' | 'down';
  categoryId: string | null;
  merchantKey: string | null;
  merchant: string | null;
  transactionId: string | null;
  amount: number | null;
  usual: number | null;
  /** (amount − usual) ÷ usual. */
  change: number | null;
  months: number | null;
  lastYear: number | null;
  soFar: number | null;
  count: number | null;
  usualCount: number | null;
  cadenceDays: number | null;
  yearly: number | null;
}

export interface Usual {
  amount: number;
  months: number;
  change: number | null;
  isUnusual: boolean;
  /** Compared up to the same day of the month (a month in progress). */
  byDay: boolean;
}

export interface MonthTotal {
  /** `yyyy-MM`. */
  month: string;
  spent: number;
  isComplete: boolean;
}

export interface CategoryTotal {
  /** `null` is spending without a category. */
  categoryId: string | null;
  amount: number;
  usual: number | null;
}

export interface Subscription {
  merchantKey: string;
  merchant: string;
  categoryId: string | null;
  cadenceDays: number;
  latestAmount: number;
  previousAmount: number;
  firstDate: string;
  lastDate: string;
  nextDate: string;
  occurrences: number;
  isActive: boolean;
  monthlyCost: number;
  yearlyCost: number;
  priceChanged: boolean;
  isNew: boolean;
}

export interface Overview {
  hasData: boolean;
  month: string | null;
  currency: string;
  earliestMonth: string | null;
  latestMonth: string | null;
  dataEnd: string | null;
  isPartial: boolean;
  /** The last day the month's numbers cover. */
  through: string | null;
  spent: number;
  income: number;
  usual: Usual | null;
  projected: number | null;
  history: MonthTotal[];
  categories: CategoryTotal[];
  insights: Insight[];
  upcoming: Subscription[];
}

export interface MerchantTotal {
  merchantKey: string;
  merchant: string;
  amount: number;
  count: number;
}

export interface CategoryDetail {
  categoryId: string;
  month: string | null;
  currency: string;
  earliestMonth: string | null;
  latestMonth: string | null;
  isPartial: boolean;
  spent: number;
  usual: number | null;
  trend: MonthTotal[];
  merchants: MerchantTotal[];
}

export interface CountedTransaction {
  transaction: TransactionItem;
  /** What it adds to the number: spending positive, a refund negative. */
  counted: number;
}

export interface CountedTransactions {
  month: string | null;
  currency: string;
  total: number;
  items: CountedTransaction[];
}

export interface Subscriptions {
  currency: string;
  monthlyTotal: number;
  yearlyTotal: number;
  items: Subscription[];
}

/** The `category` filter value for spending without a category. */
export const UNCLASSIFIED = 'none';

/** What a drill-down to transactions filters by. */
export interface SpendingFilter {
  month: string;
  category?: string;
  merchant?: string;
}
