import { HttpClient, HttpEvent } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ImportBatch } from './import.models';

export const IMPORTS_URL = '/api/imports';

@Injectable({ providedIn: 'root' })
export class ImportApi {
  private readonly http = inject(HttpClient);

  /** Uploads a statement; the events carry upload progress, then the batch summary. */
  upload(file: File): Observable<HttpEvent<ImportBatch>> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<ImportBatch>(IMPORTS_URL, body, {
      reportProgress: true,
      observe: 'events',
    });
  }
}
