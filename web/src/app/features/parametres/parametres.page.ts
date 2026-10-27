import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « parametres » (provisoire). */
@Component({
  selector: 'dk-parametres-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">parametres</h1></section>`,
})
export class SettingsPage {}
