import { Insight, Overview, Subscription } from './insights.models';

export { text, translocoTesting } from '../../shared/testing';
export { categoryList, GROCERIES, RESTAURANTS } from '../transactions/testing';

export function insight(overrides: Partial<Insight> = {}): Insight {
  return {
    type: 'categoryChange',
    score: 40,
    direction: 'up',
    categoryId: null,
    merchantKey: null,
    merchant: null,
    transactionId: null,
    amount: null,
    usual: null,
    change: null,
    months: null,
    lastYear: null,
    soFar: null,
    count: null,
    usualCount: null,
    cadenceDays: null,
    yearly: null,
    ...overrides,
  };
}

export function subscription(overrides: Partial<Subscription> = {}): Subscription {
  return {
    merchantKey: 'netflix',
    merchant: 'Netflix',
    categoryId: null,
    cadenceDays: 30,
    latestAmount: 49,
    previousAmount: 49,
    firstDate: '2026-05-05',
    lastDate: '2026-10-05',
    nextDate: '2026-11-05',
    occurrences: 6,
    isActive: true,
    monthlyCost: 49,
    yearlyCost: 588,
    priceChanged: false,
    isNew: false,
    ...overrides,
  };
}

/** A complete September against six months of history, as `GET /api/insights/overview` returns it. */
export function overview(overrides: Partial<Overview> = {}): Overview {
  return {
    hasData: true,
    month: '2026-09',
    currency: 'PLN',
    earliestMonth: '2026-04',
    latestMonth: '2026-10',
    dataEnd: '2026-10-28',
    isPartial: false,
    through: '2026-09-30',
    spent: 4383.8,
    income: 9998.25,
    usual: { amount: 4771.2, months: 5, change: -0.0812, isUnusual: false, byDay: false },
    projected: null,
    history: [
      { month: '2026-08', spent: 5037.02, isComplete: true },
      { month: '2026-09', spent: 4383.8, isComplete: true },
    ],
    categories: [
      { categoryId: '0000000c-0000-0000-0000-000000000001', amount: 1515.51, usual: 1750.49 },
      { categoryId: null, amount: 166.08, usual: null },
    ],
    insights: [],
    upcoming: [],
    ...overrides,
  };
}
