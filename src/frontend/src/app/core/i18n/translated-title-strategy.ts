import { inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

/** Route `title`s are translation keys; the document title becomes "<page> · <app name>". */
@Injectable()
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot);
    const appName = this.transloco.translate('app.name');
    this.title.setTitle(key ? `${this.transloco.translate(key)} · ${appName}` : appName);
  }
}
