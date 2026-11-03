// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ClipboardModule } from '@angular/cdk/clipboard';

@Component({
  selector: 'app-clip-board',
  imports: [CommonModule, SharedModule, ClipboardModule],
  templateUrl: './clip-board.component.html',
  styleUrls: ['./clip-board.component.scss']
})
export class ClipBoardComponent {
  value = `Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua`;
}
