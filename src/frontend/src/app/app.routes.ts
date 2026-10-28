import { isDevMode } from '@angular/core';
import { Routes } from '@angular/router';

const placeholder = () =>
  import('./features/placeholder/placeholder-page').then((m) => m.PlaceholderPage);

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  { path: 'overview', title: 'nav.overview', loadComponent: placeholder },
  { path: 'transactions', title: 'nav.transactions', loadComponent: placeholder },
  { path: 'categories', title: 'nav.categories', loadComponent: placeholder },
  { path: 'import', title: 'nav.import', loadComponent: placeholder },
  ...(isDevMode()
    ? [
        {
          path: 'styleguide',
          title: 'styleguide.title',
          loadComponent: () =>
            import('./features/styleguide/styleguide-page').then((m) => m.StyleguidePage),
        },
      ]
    : []),
  { path: '**', redirectTo: 'overview' },
];
