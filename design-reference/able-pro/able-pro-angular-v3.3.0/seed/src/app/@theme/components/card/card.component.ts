// angular import
import { Component, ContentChild, ElementRef, TemplateRef, contentChild, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-card',
  imports: [CommonModule, MatCardModule],
  templateUrl: './card.component.html',
  styleUrls: ['./card.component.scss']
})
export class CardComponent {
  // public props

  /**
   * Class to be applied at card level
   */
  cardClass = input<string>();

  /**
   * To hide header from card
   */
  showHeader = input<boolean>(true);

  /**
   * To hide content from card
   */
  showContent = input<boolean>(true);

  /**
   * Class to be applied on card header
   */
  headerClass = input<string>();

  /**
   * Class to be applied on action section of mat card
   */
  actionClass = input<string>();

  /**
   * Title of card. It will be visible at left side of card header
   */
  cardTitle = input<string>();

  /**
   * padding around card content. default in px
   */
  padding = input<number>(24); // set default to 24 px

  /**
   * Template reference of header actions on right side
   */
  readonly headerOptionsTemplate = contentChild<TemplateRef<ElementRef>>('headerOptionsTemplate');

  /**
   * Template reference of header actions besides title at left
   */
  readonly headerTitleTemplate = contentChild<TemplateRef<ElementRef>>('headerTitleTemplate');

  /**
   * Template reference for mat-actions at bottom
   */
  @ContentChild('actionTemplate') actionTemplate: TemplateRef<ElementRef>;
}
