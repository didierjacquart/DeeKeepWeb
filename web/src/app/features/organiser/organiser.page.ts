import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Page « organiser » (provisoire). */
@Component({
  selector: 'dk-organiser-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="dk-page"><h1 class="dk-page-title">organiser</h1></section>`,
})
export class OrganizePage {}
