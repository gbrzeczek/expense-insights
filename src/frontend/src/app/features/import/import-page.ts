import { DatePipe } from '@angular/common';
import { HttpErrorResponse, HttpEventType, httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { ImportApi, IMPORTS_URL } from './import.api';
import { FAILURE_CODES, FailureCode, ImportBatch, MAX_FILE_SIZE } from './import.models';
import { ImportSummaryPipe } from './import.pipes';
import { ImportResult } from './import-result';

type UploadState =
  | { kind: 'idle' }
  | { kind: 'uploading'; name: string; percent: number }
  | { kind: 'processing'; name: string }
  | { kind: 'done'; batch: ImportBatch }
  | { kind: 'failed'; code: FailureCode };

/** Upload an ING statement, see what the import did, and the earlier imports. */
@Component({
  selector: 'app-import-page',
  imports: [TranslocoDirective, DatePipe, DisplayDatePipe, ImportResult, ImportSummaryPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './import-page.html',
  styleUrl: './import-page.scss',
})
export class ImportPage {
  private readonly api = inject(ImportApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly accept = '.csv,text/csv';
  protected readonly state = signal<UploadState>({ kind: 'idle' });
  protected readonly history = httpResource<ImportBatch[]>(() => IMPORTS_URL, { defaultValue: [] });

  protected readonly uploading = computed(() => {
    const s = this.state();
    return s.kind === 'uploading' ? s : null;
  });
  protected readonly processing = computed(() => {
    const s = this.state();
    return s.kind === 'processing' ? s : null;
  });
  protected readonly failure = computed(() => {
    const s = this.state();
    return s.kind === 'failed' ? s.code : null;
  });
  protected readonly result = computed(() => {
    const s = this.state();
    return s.kind === 'done' ? s.batch : null;
  });
  protected readonly isBusy = computed(
    () => this.uploading() !== null || this.processing() !== null,
  );

  protected onFileSelected(input: HTMLInputElement): void {
    const file = input.files?.[0];
    // Reset so picking the same file again still fires `change`.
    input.value = '';
    if (!file) {
      return;
    }
    if (file.size > MAX_FILE_SIZE) {
      this.state.set({ kind: 'failed', code: 'fileTooLarge' });
      return;
    }

    this.state.set({ kind: 'uploading', name: file.name, percent: 0 });
    const subscription = this.api.upload(file).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress) {
          const percent = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
          this.state.set(
            percent >= 100
              ? { kind: 'processing', name: file.name }
              : { kind: 'uploading', name: file.name, percent },
          );
        } else if (event.type === HttpEventType.Response && event.body) {
          this.state.set({ kind: 'done', batch: event.body });
          this.history.reload();
        }
      },
      error: (error: unknown) => this.state.set({ kind: 'failed', code: failureCode(error) }),
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }
}

function failureCode(error: unknown): FailureCode {
  const code =
    error instanceof HttpErrorResponse ? (error.error as { code?: unknown })?.code : null;
  return FAILURE_CODES.find((known) => known === code) ?? 'unknown';
}
