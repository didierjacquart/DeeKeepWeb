import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Logo : carré arrondi cyan portant une croche, « DeeKeep » et sa devise. */
@Component({
  selector: 'dk-app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="icon" viewBox="0 0 128 128" aria-hidden="true">
      <rect width="128" height="128" rx="30" fill="#00e5ff" />
      <path d="M52 32 L92 24 V82 a14 14 0 1 1 -8 -12.6 V44 L60 49 V90 a14 14 0 1 1 -8 -12.6 Z" fill="#00363d" />
    </svg>
    @if (!compact()) {
      <div class="text">
        <span class="title">DeeKeep</span>
        <span class="tagline">A U D I O P H I L E &nbsp; G R A D E</span>
      </div>
    }
  `,
  styles: `
    :host { display: flex; align-items: center; gap: 12px; }
    .icon { width: 36px; height: 36px; flex: none; }
    .text { display: flex; flex-direction: column; }
    .title { font-family: var(--dk-font-title); font-size: 20px; font-weight: 800; color: var(--dk-primary); line-height: 1.1; }
    .tagline { font-size: 9px; font-weight: 600; color: var(--dk-muted); white-space: nowrap; }
  `,
})
export class AppLogo {
  readonly compact = input(false);
}
