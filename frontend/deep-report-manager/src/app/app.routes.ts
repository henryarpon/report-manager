import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'reports' },
  {
    path: 'reports',
    title: 'Reports · DEEP',
    loadComponent: () => import('./reports/reports-page').then((m) => m.ReportsPage),
  },
  { path: '**', redirectTo: 'reports' },
];
