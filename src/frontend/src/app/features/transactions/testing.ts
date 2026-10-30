import { Category } from '../../core/categories/categories.models';
import { TransactionDetail, TransactionItem } from './transactions.models';

export { text, translocoTesting } from '../../shared/testing';

export const GROCERIES = '0000000c-0000-0000-0000-000000000001';
export const RESTAURANTS = '0000000c-0000-0000-0000-000000000002';
export const HEALTH = '0000000c-0000-0000-0000-000000000004';
export const OTHER = '0000000c-0000-0000-0000-000000000014';
export const TRANSFERS_FROM_PEOPLE = '0000000c-0000-0000-0000-000000000016';
export const COFFEE = '01a1-coffee';

function global(
  id: string,
  key: string,
  sortOrder: number,
  kind: Category['kind'] = 'spending',
): Category {
  return { id, key, name: null, parentId: null, kind, sortOrder, hidden: false, own: false };
}

/** A few global categories plus one own subcategory, as `GET /api/categories` returns them. */
export function categoryList(): Category[] {
  return [
    global(GROCERIES, 'groceries', 10),
    global(RESTAURANTS, 'restaurants', 20),
    global(HEALTH, 'health', 40),
    global(OTHER, 'other', 140),
    global(TRANSFERS_FROM_PEOPLE, 'transfersFromPeople', 160, 'income'),
    {
      id: COFFEE,
      key: null,
      name: 'Kawa',
      parentId: RESTAURANTS,
      kind: 'spending',
      sortOrder: 20,
      hidden: false,
      own: true,
    },
  ];
}

export function item(overrides: Partial<TransactionItem> = {}): TransactionItem {
  return {
    id: 'tx-1',
    date: '2026-10-27',
    status: 'booked',
    amount: -62.56,
    currency: 'PLN',
    kind: 'card',
    merchant: 'JMP S.A. BIEDRONKA 3803 KRAKOW PO',
    merchantKey: 'biedronka',
    categoryId: GROCERIES,
    source: 'globalRule',
    confidence: 0.95,
    needsReview: false,
    reason: 'merchantExact',
    reasonValue: 'biedronka',
    offsetsCategoryId: null,
    ...overrides,
  };
}

export function detail(overrides: Partial<TransactionDetail> = {}): TransactionDetail {
  return {
    transaction: item(),
    title: 'Płatność kartą 27.10.2026 Nr karty 4111xx1234',
    counterpartyRaw: ' JMP S.A. BIEDRONKA 3803  KRAKOW  PO ',
    counterpartyAccount: null,
    counterpartyBank: null,
    accountName: 'KONTO Mobi pow.26',
    bookingDate: '2026-10-28',
    purchaseDate: '2026-10-27',
    originalAmount: null,
    originalCurrency: null,
    raw: ['2026-10-27', '2026-10-28', ' JMP S.A. BIEDRONKA 3803  KRAKOW  PO '],
    suggestions: [RESTAURANTS, OTHER, HEALTH],
    applyToAll: null,
    ...overrides,
  };
}
