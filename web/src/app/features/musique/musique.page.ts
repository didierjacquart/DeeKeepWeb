import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « musique » (provisoire). */
@Component({
  selector: 'dk-musique-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">musique</h1></section>`,
})
export class MusicPage {}
