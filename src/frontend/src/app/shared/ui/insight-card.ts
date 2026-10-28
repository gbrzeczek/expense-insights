import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Observation first, quantitative evidence second, optional action third (projected content).
 * Callers pass pre-computed, translated text; this component never computes numbers.
 */
@Component({
  selector: 'app-insight-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="card insight">
      @if (overline()) {
        <p class="overline">{{ overline() }}</p>
      }
      <h3 class="insight__headline">{{ headline() }}</h3>
      <p class="support">{{ evidence() }}</p>
      <div class="insight__action">
        <ng-content />
      </div>
    </article>
  `,
  styles: `
    .insight {
      display: grid;
      gap: var(--space-3);
      background: var(--color-surface);
      border-left: 3px solid var(--color-accent);
    }
    .insight__headline {
      font-family: var(--font-editorial);
      font-size: var(--text-page);
      font-weight: var(--weight-regular);
      line-height: var(--leading-heading);
      letter-spacing: var(--tracking-heading);
    }
    .insight__action:empty {
      display: none;
    }
  `,
})
export class InsightCard {
  readonly overline = input('');
  readonly headline = input.required<string>();
  readonly evidence = input.required<string>();
}
