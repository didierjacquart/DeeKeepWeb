import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { AppLogo } from '../../layout/app-logo';

/** Connexion, ou définition du mot de passe à la toute première visite (route /setup). */
@Component({
  selector: 'dk-login',
  imports: [FormsModule, AppLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="box dk-card" (ngSubmit)="submit()">
      <dk-app-logo />
      @if (setup()) {
        <p class="dk-help">Première visite : choisissez le mot de passe qui protégera DeeKeep (8 caractères au moins).</p>
      }
      <input class="dk-input" type="password" name="password" autocomplete="current-password"
             [placeholder]="setup() ? 'Nouveau mot de passe' : 'Mot de passe'" [(ngModel)]="password" required autofocus />
      @if (setup()) {
        <input class="dk-input" type="password" name="confirm" autocomplete="new-password" placeholder="Confirmer" [(ngModel)]="confirm" required />
      }
      @if (error()) { <p class="dk-error-text">{{ error() }}</p> }
      <button class="primary-btn" type="submit" [disabled]="busy()">{{ setup() ? 'Enregistrer' : 'Se connecter' }}</button>
    </form>
  `,
  styles: `
    :host { display: grid; place-items: center; min-height: 100dvh; padding: 16px; }
    .box { width: min(380px, 100%); display: flex; flex-direction: column; gap: 14px; }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly setup = computed(() => this.route.snapshot.routeConfig?.path === 'setup');
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  password = '';
  confirm = '';

  async submit(): Promise<void> {
    this.error.set(null);
    if (this.setup() && this.password !== this.confirm) {
      this.error.set('Les deux saisies diffèrent.');
      return;
    }
    this.busy.set(true);
    const failure = this.setup() ? await this.auth.setup(this.password) : await this.auth.login(this.password);
    this.busy.set(false);
    if (failure) this.error.set(failure);
    else await this.router.navigateByUrl('/');
  }
}
