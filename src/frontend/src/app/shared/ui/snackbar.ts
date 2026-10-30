import { ChangeDetectionStrategy, Component, inject, Injectable, signal } from '@angular/core';

export interface SnackbarMessage {
  text: string;
  /** An optional action, e.g. "Zastosuj do wszystkich". */
  actionLabel?: string;
  action?: () => void;
}

/** One message at a time at the bottom of the screen; it hides itself after a while. */
@Injectable({ providedIn: 'root' })
export class Snackbar {
  static readonly durationMs = 8000;

  private readonly state = signal<SnackbarMessage | null>(null);
  private timer: ReturnType<typeof setTimeout> | undefined;

  readonly message = this.state.asReadonly();

  show(message: SnackbarMessage): void {
    clearTimeout(this.timer);
    this.state.set(message);
    this.timer = setTimeout(() => this.dismiss(), Snackbar.durationMs);
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.state.set(null);
  }

  /** Runs the message's action and hides it. */
  act(): void {
    const action = this.state()?.action;
    this.dismiss();
    action?.();
  }
}

/** Renders the {@link Snackbar} message. Place once, in the shell. */
@Component({
  selector: 'app-snackbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="snackbar__region" role="status" aria-live="polite">
      @if (snackbar.message(); as m) {
        <div class="snackbar">
          <p class="snackbar__text">{{ m.text }}</p>
          @if (m.actionLabel) {
            <button type="button" class="snackbar__action" (click)="snackbar.act()">
              {{ m.actionLabel }}
            </button>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .snackbar__region {
      position: fixed;
      inset: auto var(--gutter)
        calc(var(--nav-height) + var(--space-3) + env(safe-area-inset-bottom));
      z-index: 30;
      display: grid;
      justify-items: center;
      pointer-events: none;
    }
    .snackbar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-4);
      width: min(100%, 560px);
      padding: var(--space-3) var(--space-4);
      border-radius: var(--radius-card);
      background: var(--color-ink);
      color: var(--color-bg);
      box-shadow: var(--shadow-overlay);
      pointer-events: auto;
    }
    .snackbar__text {
      flex: 1 1 12rem;
      font-size: var(--text-support);
    }
    .snackbar__action {
      min-height: var(--touch-target);
      padding: 0 var(--space-3);
      border: 1px solid var(--color-bg);
      border-radius: var(--radius-button);
      background: transparent;
      color: var(--color-bg);
      font: inherit;
      font-size: var(--text-support);
      font-weight: var(--weight-medium);
      cursor: pointer;
    }
    @media (min-width: 1024px) {
      .snackbar__region {
        inset-block-end: var(--space-6);
      }
    }
  `,
})
export class SnackbarHost {
  protected readonly snackbar = inject(Snackbar);
}
