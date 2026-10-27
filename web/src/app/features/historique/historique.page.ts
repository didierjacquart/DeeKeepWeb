import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « historique » (provisoire). */
@Component({
  selector: 'dk-historique-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">historique</h1></section>`,
})
export class HistoryPage {}
