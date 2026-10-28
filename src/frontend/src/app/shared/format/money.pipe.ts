import { formatCurrency, getCurrencySymbol } from '@angular/common';
import { inject, LOCALE_ID, Pipe, PipeTransform } from '@angular/core';

export type MoneySign = 'auto' | 'always';

const MINUS = '\u2212';

/**
 * Formats an amount computed by the backend, e.g. `-1234.5` → `−1 234,50 zł`.
 * Accepts strings because the API serializes decimals as strings to keep precision.
 * `sign: 'always'` adds `+` to positive amounts, so income and outflow differ without colour.
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(
    amount: number | string | null | undefined,
    currency = 'PLN',
    sign: MoneySign = 'auto',
  ): string {
    if (amount === null || amount === undefined || amount === '') {
      return '';
    }
    const value = typeof amount === 'string' ? Number(amount) : amount;
    if (!Number.isFinite(value)) {
      return '';
    }
    const symbol = getCurrencySymbol(currency, 'narrow', this.locale);
    const formatted = formatCurrency(value, this.locale, symbol, currency, '1.2-2');
    if (formatted.startsWith('-')) {
      return MINUS + formatted.slice(1);
    }
    return sign === 'always' && value > 0 ? `+${formatted}` : formatted;
  }
}
