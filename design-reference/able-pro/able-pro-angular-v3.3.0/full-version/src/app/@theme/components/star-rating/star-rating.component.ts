// Angular Import
import { Component, ChangeDetectionStrategy, ViewEncapsulation, input, output, computed } from '@angular/core';
import { CommonModule } from '@angular/common';

// Project Import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-star-rating',
  imports: [CommonModule, SharedModule],
  templateUrl: './star-rating.component.html',
  styleUrls: ['./star-rating.component.scss'],
  encapsulation: ViewEncapsulation.Emulated,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StarRatingComponent {
  readonly rating = input<number>(2);
  readonly starCount = input<number>(5);
  readonly ratingUpdated = output<number>();

  // Computed property for rating array
  ratingArr = computed(() => {
    const count = this.starCount();
    return Array.from({ length: count }, (_, i) => i);
  });

  onClick(rating: number) {
    this.ratingUpdated.emit(rating);
    return false;
  }

  showIcon(i: number) {
    if (this.rating() >= i + 1) {
      return 'star';
    } else {
      return 'star_border';
    }
  }
}
