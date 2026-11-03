// Angular Import
import { Component, OnInit, ViewEncapsulation, inject, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';

// Project Import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-star-rating',
  imports: [CommonModule, SharedModule],
  templateUrl: './star-rating.component.html',
  styleUrls: ['./star-rating.component.scss'],
  encapsulation: ViewEncapsulation.Emulated
})
export class StarRatingComponent implements OnInit {
  private snackBar = inject(MatSnackBar);

  readonly rating = input<number>(2);
  readonly starCount = input<number>(5);
  readonly ratingUpdated = output<number>();

  // eslint-disable-next-line
  ratingArr: any = [];

  ngOnInit() {
    for (let i = 0; i < this.starCount(); i++) {
      this.ratingArr.push(i);
    }
  }
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
