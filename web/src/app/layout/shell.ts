import { ChangeDetectionStrategy, Component, Injectable, computed, inject, signal } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltipModule } from '@angular/material/tooltip';
import { filter, map } from 'rxjs';
import { AppLogo } from './app-logo';
import { JobsPanel } from './jobs-panel';
import { ConnectivityService } from '../core/offline/connectivity.service';
import { OfflineQueueService } from '../core/offline/offline-queue.service';
import { JobsService } from '../core/jobs/jobs.service';
import { PlayerBar } from '../core/player/player-bar';
import { GqlService } from '../core/graphql/gql.service';

/** Compteurs affichés à côté des entrées de navigation (ex. Historique). Les pages les renseignent. */
@Injectable({ providedIn: 'root' })
export class NavBadges {
  readonly history = signal<number | null>(null);
}

interface NavItem {
  path: string;
  label: string;
  icon: string;
  sub?: boolean;
}

/**
 * Coquille de l'application : barre latérale fixe sur ordinateur (250 px),
 * tiroir sur mobile ; pied de barre avec l'état de la liaison, les actions en
 * attente et les tâches de fond ; lecteur en bas d'écran.
 */
@Component({
  selector: 'dk-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatTooltipModule, AppLogo, JobsPanel, PlayerBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  readonly connectivity = inject(ConnectivityService);
  readonly queue = inject(OfflineQueueService);
  readonly jobs = inject(JobsService);
  readonly badges = inject(NavBadges);
  private readonly router = inject(Router);
  // Instancié dès le démarrage : il rafraîchit les requêtes à chaque reconnexion.
  private readonly gql = inject(GqlService);

  readonly mobile = toSignal(inject(BreakpointObserver).observe('(max-width: 899px)').pipe(map(r => r.matches)), { initialValue: false });
  readonly drawerOpen = signal(false);
  readonly toolsOpen = signal(true);
  readonly jobsOpen = signal(false);

  readonly main: NavItem[] = [
    { path: '/musique', label: 'Musique', icon: '♫' },
    { path: '/bibliotheque', label: 'Bibliothèque', icon: '▤' },
  ];
  readonly tools: NavItem[] = [
    { path: '/reencoder', label: 'Réencoder', icon: '⇄', sub: true },
    { path: '/organiser', label: 'Organiser', icon: '⌂', sub: true },
    { path: '/qualite', label: 'Qualité', icon: '◎', sub: true },
    { path: '/diagnostic', label: 'Diagnostic', icon: '✚', sub: true },
  ];
  readonly bottom: NavItem[] = [
    { path: '/historique', label: 'Historique', icon: '↺' },
    { path: '/parametres', label: 'Paramètres', icon: '⚙' },
  ];

  private readonly url = toSignal(
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(e => (e as NavigationEnd).urlAfterRedirects)),
    { initialValue: this.router.url },
  );

  readonly title = computed(() => {
    const all = [...this.main, ...this.tools, ...this.bottom];
    return all.find(i => this.url().startsWith(i.path))?.label ?? 'DeeKeep';
  });

  readonly linkLabel = computed(() => {
    switch (this.connectivity.state()) {
      case 'online': return 'Serveur connecté';
      case 'reconnecting': return 'Reconnexion…';
      default: return 'Hors ligne';
    }
  });

  constructor() {
    // Afficher une sous-page du groupe « Outils » le redéplie (comme l'original).
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      if (this.tools.some(t => this.router.url.startsWith(t.path))) this.toolsOpen.set(true);
      if (this.mobile()) this.drawerOpen.set(false);
    });
  }
}
