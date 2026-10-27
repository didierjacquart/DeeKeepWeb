import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/** Messages éphémères (bas d'écran). Les erreurs restent plus longtemps. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snack = inject(MatSnackBar);

  info(message: string): void {
    this.snack.open(message, 'OK', { duration: 3500 });
  }

  success(message: string): void {
    this.snack.open(message, 'OK', { duration: 3000 });
  }

  error(message: string): void {
    this.snack.open(message, 'Fermer', { duration: 8000 });
  }
}
