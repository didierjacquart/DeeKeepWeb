import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « diagnostic » (provisoire). */
@Component({
  selector: 'dk-diagnostic-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">diagnostic</h1></section>`,
})
export class DiagnosticsPage {}
