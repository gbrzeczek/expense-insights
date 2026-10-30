import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ApplyToAllResponse,
  CorrectionResponse,
  MerchantReview,
  Rule,
  TransactionDetail,
  TransactionFilter,
  TransactionItem,
  TransactionPage,
} from './transactions.models';

export const TRANSACTIONS_URL = '/api/transactions';
export const MERCHANTS_REVIEW_URL = '/api/merchants/to-review';
export const RULES_URL = '/api/rules';
export const PAGE_SIZE = 50;

export interface TransactionQuery {
  filter: TransactionFilter;
  q: string;
  offset: number;
  limit?: number;
}

@Injectable({ providedIn: 'root' })
export class TransactionsApi {
  private readonly http = inject(HttpClient);

  list(query: TransactionQuery): Observable<TransactionPage> {
    let params = new HttpParams()
      .set('filter', query.filter)
      .set('offset', query.offset)
      .set('limit', query.limit ?? PAGE_SIZE);
    if (query.q.trim()) {
      params = params.set('q', query.q.trim());
    }
    return this.http.get<TransactionPage>(TRANSACTIONS_URL, { params });
  }

  detail(id: string): Observable<TransactionDetail> {
    return this.http.get<TransactionDetail>(`${TRANSACTIONS_URL}/${id}`);
  }

  correct(id: string, categoryId: string): Observable<CorrectionResponse> {
    return this.http.put<CorrectionResponse>(`${TRANSACTIONS_URL}/${id}/category`, { categoryId });
  }

  applyToAll(id: string): Observable<ApplyToAllResponse> {
    return this.http.post<ApplyToAllResponse>(`${TRANSACTIONS_URL}/${id}/apply-to-all`, null);
  }

  setOffsets(id: string, categoryId: string | null): Observable<TransactionItem> {
    return this.http.put<TransactionItem>(`${TRANSACTIONS_URL}/${id}/offsets`, { categoryId });
  }

  merchantsToReview(): Observable<MerchantReview> {
    return this.http.get<MerchantReview>(MERCHANTS_REVIEW_URL);
  }

  labelMerchant(merchantKey: string, categoryId: string): Observable<ApplyToAllResponse> {
    return this.http.post<ApplyToAllResponse>('/api/merchants/category', {
      merchantKey,
      categoryId,
    });
  }

  rules(): Observable<Rule[]> {
    return this.http.get<Rule[]>(RULES_URL);
  }

  deleteRule(id: string): Observable<{ changed: number }> {
    return this.http.delete<{ changed: number }>(`${RULES_URL}/${id}`);
  }
}
