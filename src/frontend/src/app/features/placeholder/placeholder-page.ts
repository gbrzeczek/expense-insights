import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { map } from 'rxjs';

/** Temporary page for sections that later plans fill in. */
@Component({
  selector: 'app-placeholder-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section *transloco="let t" class="page">
      <h1 class="page-title">{{ t(titleKey() ?? 'app.name') }}</h1>
      <p class="support">{{ t('placeholder.comingSoon') }}</p>
    </section>
  `,
  styles: `
    .page {
      display: grid;
      gap: var(--space-2);
    }
  `,
})
export class PlaceholderPage {
  protected readonly titleKey = toSignal(
    inject(ActivatedRoute).title.pipe(map((title) => title as string | undefined)),
  );
}
