import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.service';
import { LoginPage } from './core/auth/login.page';
import { Shell } from './layout/shell';

export const routes: Routes = [
  { path: 'login', component: LoginPage },
  { path: 'setup', component: LoginPage },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'musique' },
      { path: 'musique', loadComponent: () => import('./features/musique/musique.page').then(m => m.MusicPage) },
      { path: 'bibliotheque', loadComponent: () => import('./features/bibliotheque/bibliotheque.page').then(m => m.LibraryPage) },
      { path: 'historique', loadComponent: () => import('./features/historique/historique.page').then(m => m.HistoryPage) },
      { path: 'reencoder', loadComponent: () => import('./features/reencoder/reencoder.page').then(m => m.ReencodePage) },
      { path: 'organiser', loadComponent: () => import('./features/organiser/organiser.page').then(m => m.OrganizePage) },
      { path: 'qualite', loadComponent: () => import('./features/qualite/qualite.page').then(m => m.QualityPage) },
      { path: 'parametres', loadComponent: () => import('./features/parametres/parametres.page').then(m => m.SettingsPage) },
      { path: 'diagnostic', loadComponent: () => import('./features/diagnostic/diagnostic.page').then(m => m.DiagnosticsPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
