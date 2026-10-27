import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « bibliotheque » (provisoire). */
@Component({
  selector: 'dk-bibliotheque-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">bibliotheque</h1></section>`,
})
export class LibraryPage {}
