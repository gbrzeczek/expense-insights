// Fictional data for the style guide only. Amounts are in grosze to keep sums exact.

export interface SampleCategory {
  readonly name: string;
  readonly colorIndex: number;
  readonly currentGrosze: number;
  readonly previousGrosze: number;
}

export const SAMPLE_CATEGORIES: readonly SampleCategory[] = [
  { name: 'Spożywcze', colorIndex: 1, currentGrosze: 141_235, previousGrosze: 149_810 },
  { name: 'Rachunki', colorIndex: 3, currentGrosze: 98_640, previousGrosze: 97_215 },
  { name: 'Restauracje', colorIndex: 2, currentGrosze: 52_480, previousGrosze: 57_040 },
  { name: 'Transport', colorIndex: 4, currentGrosze: 43_815, previousGrosze: 51_260 },
  { name: 'Zdrowie', colorIndex: 5, currentGrosze: 21_290, previousGrosze: 18_905 },
  { name: 'Inne', colorIndex: 6, currentGrosze: 71_100, previousGrosze: 91_590 },
];

export interface SampleTransaction {
  readonly merchant: string;
  readonly date: string;
  readonly category: string;
  readonly colorIndex: number;
  readonly amountGrosze: number;
}

export const SAMPLE_TRANSACTIONS: readonly SampleTransaction[] = [
  {
    merchant: 'Biedronka',
    date: '2026-10-28',
    category: 'Spożywcze',
    colorIndex: 1,
    amountGrosze: -8_427,
  },
  {
    merchant: 'Przykładowa Firma Sp. z o.o.',
    date: '2026-10-27',
    category: 'Wynagrodzenie',
    colorIndex: 6,
    amountGrosze: 620_000,
  },
  {
    merchant: 'Orlen',
    date: '2026-10-26',
    category: 'Transport',
    colorIndex: 4,
    amountGrosze: -23_150,
  },
  {
    merchant: 'Pyszne.pl',
    date: '2026-10-25',
    category: 'Restauracje',
    colorIndex: 2,
    amountGrosze: -6_790,
  },
  {
    merchant: 'PGE Obrót',
    date: '2026-10-24',
    category: 'Rachunki',
    colorIndex: 3,
    amountGrosze: -31_244,
  },
];

export const SAMPLE_MONTH = '2026-10-01';
