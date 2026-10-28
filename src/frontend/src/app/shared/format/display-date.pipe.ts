import { formatDate } from '@angular/common';
import { inject, LOCALE_ID, Pipe, PipeTransform } from '@angular/core';

/** Formats a date (`DateOnly` arrives as `yyyy-MM-dd`) as `28.10.2026`. */
@Pipe({ name: 'displayDate' })
export class DisplayDatePipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: string | Date | null | undefined): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }
    return formatDate(value, 'dd.MM.yyyy', this.locale);
  }
}
