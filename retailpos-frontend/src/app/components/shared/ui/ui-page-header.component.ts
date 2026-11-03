import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface UiBreadcrumb {
  label: string;
  /** Router link for ancestors; omit it (or leave the item last) for the current page. */
  link?: string | unknown[];
}

/**
 * Page header: breadcrumbs, title, description and action areas.
 *
 * ```html
 * <app-ui-page-header title="Products" description="Manage the catalogue"
 *   [breadcrumbs]="[{ label: 'Inventory', link: '/inventory' }, { label: 'Products' }]">
 *   <button uiPageSecondaryActions appUiButton variant="outline">Export</button>
 *   <a uiPagePrimaryActions appUiButton routerLink="/products/new">Add product</a>
 * </app-ui-page-header>
 * ```
 *
 * Actions are projected so the page keeps its own permission checks (for example `@if (canCreate)`)
 * and handlers; the header only lays them out (inline on wide screens, stacked and full width on phones).
 */
@Component({
  selector: 'app-ui-page-header',
  // A static title="" would otherwise stay on the host and show a native tooltip over the whole header.
  host: { '[attr.title]': 'null' },
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (breadcrumbs().length) {
      <nav class="ui-page-header__breadcrumbs" aria-label="Breadcrumb">
        <ol>
          @for (crumb of breadcrumbs(); track $index; let last = $last) {
            <li>
              @if (crumb.link && !last) {
                <a [routerLink]="crumb.link">{{ crumb.label }}</a>
              } @else {
                <span [attr.aria-current]="last ? 'page' : null">{{ crumb.label }}</span>
              }
            </li>
          }
        </ol>
      </nav>
    }
    <div class="ui-page-header__row">
      <div class="ui-page-header__titles">
        @switch (headingLevel()) {
          @case (2) { <h2 class="ui-page-header__title">{{ title() }}</h2> }
          @default { <h1 class="ui-page-header__title">{{ title() }}</h1> }
        }
        @if (description()) {
          <p class="ui-page-header__description">{{ description() }}</p>
        }
      </div>
      <div class="ui-page-header__actions">
        <div class="ui-page-header__group"><ng-content select="[uiPageSecondaryActions]" /></div>
        <div class="ui-page-header__group"><ng-content select="[uiPagePrimaryActions]" /></div>
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      margin-bottom: var(--rp-space-6);
      font-family: var(--rp-font-family-base);
    }

    .ui-page-header__breadcrumbs {
      margin-bottom: var(--rp-space-2);
      font-size: var(--rp-text-breadcrumb-size);
      line-height: var(--rp-text-label-line);
    }

    .ui-page-header__breadcrumbs ol {
      display: flex;
      flex-wrap: wrap;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .ui-page-header__breadcrumbs li + li::before {
      content: '/' / '';
      padding: 0 var(--rp-space-2);
      color: var(--rp-color-text-secondary);
    }

    .ui-page-header__breadcrumbs a {
      color: var(--rp-color-text-secondary);
      text-decoration: none;
    }

    .ui-page-header__breadcrumbs a:hover {
      color: var(--rp-color-text-link);
      text-decoration: underline;
    }

    .ui-page-header__breadcrumbs a:focus-visible {
      border-radius: var(--rp-radius-sm);
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    .ui-page-header__breadcrumbs [aria-current='page'] {
      color: var(--rp-color-text-primary);
    }

    .ui-page-header__row {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--rp-space-4);
    }

    .ui-page-header__titles {
      flex: 1 1 18rem;
      min-width: 0;
    }

    .ui-page-header__title {
      margin: 0;
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-page-title-size);
      font-weight: var(--rp-font-weight-semibold);
      line-height: var(--rp-text-page-title-line);
    }

    .ui-page-header__description {
      margin: var(--rp-space-1) 0 0;
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-body-size);
      line-height: var(--rp-text-body-line);
    }

    .ui-page-header__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ui-page-header__group {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ui-page-header__group:empty {
      display: none;
    }

    @media (max-width: 639.98px) {
      .ui-page-header__actions,
      .ui-page-header__group {
        flex-direction: column;
        align-items: stretch;
        width: 100%;
      }

      .ui-page-header__actions {
        flex-direction: column-reverse;
      }

      /* Projected actions live in the consumer's view, hence ::ng-deep. */
      .ui-page-header__group ::ng-deep > * {
        width: 100%;
      }
    }
  `]
})
export class UiPageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input('');
  readonly breadcrumbs = input<UiBreadcrumb[]>([]);
  /** 1 for a page title (default); use 2 when the header sits inside a tab or section. */
  readonly headingLevel = input<1 | 2>(1);
}
