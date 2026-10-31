import { formatDate, formatPercent } from '@angular/common';
import { inject, Injectable, LOCALE_ID } from '@angular/core';
import { Params } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { CategoriesStore } from '../../core/categories/categories.store';
import { formatMoney } from '../../shared/format/money.pipe';
import { pluralCategory } from '../import/plural';
import { Insight, UNCLASSIFIED } from './insights.models';

/** An insight worded for a card: observation, evidence, and where its numbers lead. */
export interface InsightView {
  readonly overline: string;
  readonly headline: string;
  readonly evidence: string;
  readonly action: string;
  readonly link: string;
  readonly queryParams: Params;
}

/**
 * Words insights and month phrases through i18n templates (with Polish plural forms). It only
 * formats numbers the backend computed: amounts, percentages and counts are never derived here.
 */
@Injectable({ providedIn: 'root' })
export class InsightText {
  private readonly transloco = inject(TranslocoService);
  private readonly categories = inject(CategoriesStore);
  private readonly locale = inject(LOCALE_ID);

  describe(insight: Insight, month: string, currency: string): InsightView {
    const t = (key: string, params: Record<string, unknown> = {}) =>
      this.transloco.translate<string>(`insights.insight.${key}`, params);
    const money = (amount: number | null) => formatMoney(amount, this.locale, currency);
    const values = {
      merchant: insight.merchant ?? '',
      amount: money(insight.amount),
      usual: money(insight.usual),
      soFar: money(insight.soFar),
      lastYear: money(insight.lastYear),
      yearly: money(insight.yearly),
      percent: this.percent(insight.change),
      months: insight.months ?? 0,
      count: insight.count ?? 0,
      usualCount: insight.usualCount ?? 0,
      cadence: this.transloco.translate<string>(`insights.cadence.${insight.cadenceDays}`),
    };
    const category = insight.categoryId ? this.categories.label(insight.categoryId) : '';
    const transactions = (params: Params) => ({
      action: this.transloco.translate<string>('insights.showTransactions'),
      link: '/overview/transactions',
      queryParams: { month, ...params },
    });

    switch (insight.type) {
      case 'categoryChange': {
        const evidence =
          insight.change === null
            ? t('categoryChange.evidence.fromZero', values)
            : t(`categoryChange.evidence.${insight.direction}`, values);
        const lastYear =
          insight.lastYear === null
            ? ''
            : ` ${t(`categoryChange.lastYear.${insight.direction}`, values)}`;
        return {
          overline: category,
          headline: t(`categoryChange.${insight.direction}`),
          evidence: evidence + lastYear,
          ...transactions({ category: insight.categoryId }),
        };
      }
      case 'pace':
        return {
          overline: t('pace.overline'),
          headline: t(`pace.${insight.direction}`),
          evidence: t('pace.evidence', values),
          ...transactions({}),
        };
      case 'largePayment':
        return {
          overline: category,
          headline: t('largePayment.headline', values),
          evidence: t('largePayment.evidence', values),
          ...transactions({ category: insight.categoryId, merchant: insight.merchantKey }),
        };
      case 'frequentSmall':
        return {
          overline: category,
          headline: t('frequentSmall.headline', values),
          evidence: t(`frequentSmall.evidence.${this.plural(values.count)}`, values),
          ...transactions({ merchant: insight.merchantKey }),
        };
      case 'newSubscription':
        return {
          overline: category,
          headline: t('newSubscription.headline', values),
          evidence: t('newSubscription.evidence', values),
          action: this.transloco.translate<string>('insights.showSubscriptions'),
          link: '/overview/subscriptions',
          queryParams: {},
        };
      case 'subscriptionPriceChange':
        return {
          overline: category,
          headline: t('subscriptionPriceChange.headline', values),
          evidence: t(`subscriptionPriceChange.evidence.${insight.direction}`, values),
          ...transactions({ merchant: insight.merchantKey }),
        };
      case 'uncategorized':
        return {
          overline: t('uncategorized.overline'),
          headline: t(`uncategorized.headline.${this.plural(values.count)}`, values),
          evidence: t('uncategorized.evidence', values),
          ...transactions({ category: UNCLASSIFIED }),
          action: t('uncategorized.action'),
        };
    }
  }

  /** `październik 2026`. */
  monthName(month: string): string {
    return formatDate(`${month}-01`, 'LLLL yyyy', this.locale);
  }

  /** `w październiku` (`W październiku` with `capitalized`). */
  inMonth(month: string, capitalized = false): string {
    const phrase = this.transloco.translate<string>(
      `insights.months.in.${Number(month.slice(5, 7))}`,
    );
    return capitalized ? phrase.charAt(0).toLocaleUpperCase(this.locale) + phrase.slice(1) : phrase;
  }

  /** `28 października`. */
  dayOfMonth(date: string): string {
    return formatDate(date, 'd MMMM', this.locale);
  }

  /** A change ratio as an unsigned whole percent (`8%`); the wording carries the direction. */
  percent(change: number | null): string {
    return change === null ? '' : formatPercent(Math.abs(change), this.locale, '1.0-0');
  }

  plural(count: number): Intl.LDMLPluralRule {
    return pluralCategory(count, this.locale);
  }
}
