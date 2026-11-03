// Angular Import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-badge',
  imports: [CommonModule, SharedModule],
  templateUrl: './badge.component.html',
  styleUrls: ['./badge.component.scss']
})
export class BadgeComponent {
  // public props
  hidden = false;

  toggleBadgeVisibility() {
    this.hidden = !this.hidden;
  }
}
