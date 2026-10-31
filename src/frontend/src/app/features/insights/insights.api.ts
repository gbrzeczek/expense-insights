import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CategoryDetail,
  CountedTransactions,
  Overview,
  SpendingFilter,
  Subscriptions,
} from './insights.models';

export const INSIGHTS_URL = '/api/insights';

@Injectable({ providedIn: 'root' })
export class InsightsApi {
  private readonly http = inject(HttpClient);

  /** The latest month when `month` is `null`. */
  overview(month: string | null): Observable<Overview> {
    return this.http.get<Overview>(`${INSIGHTS_URL}/overview`, { params: monthParams(month) });
  }

  category(id: string, month: string | null): Observable<CategoryDetail> {
    return this.http.get<CategoryDetail>(`${INSIGHTS_URL}/categories/${id}`, {
      params: monthParams(month),
    });
  }

  transactions(filter: SpendingFilter): Observable<CountedTransactions> {
    let params = new HttpParams().set('month', filter.month);
    if (filter.category) {
      params = params.set('category', filter.category);
    }
    if (filter.merchant) {
      params = params.set('merchant', filter.merchant);
    }
    return this.http.get<CountedTransactions>(`${INSIGHTS_URL}/transactions`, { params });
  }

  subscriptions(): Observable<Subscriptions> {
    return this.http.get<Subscriptions>(`${INSIGHTS_URL}/subscriptions`);
  }
}

function monthParams(month: string | null): HttpParams {
  return month ? new HttpParams().set('month', month) : new HttpParams();
}
