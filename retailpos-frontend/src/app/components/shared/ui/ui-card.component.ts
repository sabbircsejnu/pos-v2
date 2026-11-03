import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiStateComponent } from './ui-state.component';

/**
 * Card container (Able Pro: 1px neutral border, 12px radius, flat) with optional header, footer and
 * loading presentation. Projection slots:
 *
 * ```html
 * <app-ui-card title="Stock summary" subtitle="Updated hourly">
 *   <button uiCardActions appUiButton variant="outline" size="sm">Export</button>
 *   ...content...
 *   <div uiCardFooter>...</div>
 * </app-ui-card>
 * ```
 *
 * `loading` swaps the content for the standard loading state (content stays alive underneath so
 * feature state is not recreated). Empty and error content are composed with `app-ui-state`.
 */
@Component({
  selector: 'app-ui-card',
  standalone: true,
  imports: [UiStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.aria-busy]': 'loading() ? "true" : null',
    '[attr.title]': 'null'
  },
  template: `
    @if (title()) {
      <div class="ui-card__header">
        <div class="ui-card__heading">
          @switch (headingLevel()) {
            @case (3) { <h3 class="ui-card__title">{{ title() }}</h3> }
            @case (4) { <h4 class="ui-card__title">{{ title() }}</h4> }
            @default { <h2 class="ui-card__title">{{ title() }}</h2> }
          }
          @if (subtitle()) {
            <p class="ui-card__subtitle">{{ subtitle() }}</p>
          }
        </div>
        <div class="ui-card__actions"><ng-content select="[uiCardActions]" /></div>
      </div>
    }
    @if (loading()) {
      <app-ui-state kind="loading" [title]="loadingLabel()" [compact]="true" />
    }
    <div class="ui-card__body" [class.ui-card__body--flush]="!padded()" [hidden]="loading()"><ng-content /></div>
    <div class="ui-card__footer"><ng-content select="[uiCardFooter]" /></div>
  `,
  styles: [`
    :host {
      display: block;
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-card);
      background: var(--rp-color-surface-card);
      box-shadow: var(--rp-shadow-none);
      color: var(--rp-color-text-primary);
      font-family: var(--rp-font-family-base);
      font-size: var(--rp-text-body-size);
      line-height: var(--rp-text-body-line);
    }

    .ui-card__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--rp-space-3);
      padding: var(--rp-space-4) var(--rp-card-padding);
      border-bottom: 1px solid var(--rp-color-border);
    }

    .ui-card__title {
      margin: 0;
      font-size: var(--rp-text-section-size);
      font-weight: var(--rp-font-weight-semibold);
      line-height: var(--rp-text-section-line);
    }

    .ui-card__subtitle {
      margin: 0;
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
      line-height: var(--rp-text-helper-line);
    }

    .ui-card__actions {
      display: flex;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ui-card__actions:empty {
      display: none;
    }

    .ui-card__body {
      padding: var(--rp-card-padding);
    }

    .ui-card__body--flush {
      padding: 0;
    }

    .ui-card__body[hidden] {
      display: none;
    }

    .ui-card__footer {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: var(--rp-space-2);
      padding: var(--rp-space-3) var(--rp-card-padding);
      border-top: 1px solid var(--rp-color-border);
      border-radius: 0 0 var(--rp-radius-card) var(--rp-radius-card);
      background: var(--rp-color-surface-muted);
    }

    .ui-card__footer:empty {
      display: none;
    }
  `]
})
export class UiCardComponent {
  readonly title = input('');
  readonly subtitle = input('');
  /** Heading level of the title (2 by default) so the card fits the page outline. */
  readonly headingLevel = input<2 | 3 | 4>(2);
  readonly loading = input(false);
  readonly loadingLabel = input('Loading…');
  /** Set false for edge-to-edge content such as tables. */
  readonly padded = input(true);
}
