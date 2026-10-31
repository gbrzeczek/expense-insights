import { isDevMode } from '@angular/core';
import { Routes } from '@angular/router';
import { authGuard, guestGuard, ownerGuard } from './core/auth/auth.guards';
import { Shell } from './core/layout/shell';

export const routes: Routes = [
  {
    path: 'login',
    title: 'auth.login.title',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        title: 'nav.overview',
        loadComponent: () =>
          import('./features/insights/overview-page').then((m) => m.OverviewPage),
      },
      {
        path: 'overview/categories/:id',
        title: 'nav.overview',
        loadComponent: () =>
          import('./features/insights/category-page').then((m) => m.CategoryPage),
      },
      {
        path: 'overview/transactions',
        title: 'insights.transactions.title',
        loadComponent: () =>
          import('./features/insights/spending-transactions-page').then(
            (m) => m.SpendingTransactionsPage,
          ),
      },
      {
        path: 'overview/subscriptions',
        title: 'insights.subscriptions.title',
        loadComponent: () =>
          import('./features/insights/subscriptions-page').then((m) => m.SubscriptionsPage),
      },
      {
        path: 'transactions',
        title: 'nav.transactions',
        loadComponent: () =>
          import('./features/transactions/transactions-page').then((m) => m.TransactionsPage),
      },
      {
        path: 'transactions/review',
        title: 'review.title',
        loadComponent: () =>
          import('./features/transactions/review-page').then((m) => m.ReviewPage),
      },
      {
        path: 'transactions/merchants',
        title: 'merchants.title',
        loadComponent: () =>
          import('./features/transactions/merchants-page').then((m) => m.MerchantsPage),
      },
      {
        path: 'categories',
        title: 'nav.categories',
        loadComponent: () =>
          import('./features/categories/categories-page').then((m) => m.CategoriesPage),
      },
      {
        path: 'import',
        title: 'nav.import',
        loadComponent: () => import('./features/import/import-page').then((m) => m.ImportPage),
      },
      {
        path: 'account',
        title: 'nav.account',
        loadComponent: () => import('./features/account/account-page').then((m) => m.AccountPage),
      },
      {
        path: 'invitations',
        title: 'invitations.title',
        canActivate: [ownerGuard],
        loadComponent: () =>
          import('./features/invitations/invitations-page').then((m) => m.InvitationsPage),
      },
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
    ],
  },
  { path: '**', redirectTo: 'overview' },
];
