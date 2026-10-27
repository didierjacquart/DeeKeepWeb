import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « qualite » (provisoire). */
@Component({
  selector: 'dk-qualite-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">qualite</h1></section>`,
})
export class QualityPage {}
