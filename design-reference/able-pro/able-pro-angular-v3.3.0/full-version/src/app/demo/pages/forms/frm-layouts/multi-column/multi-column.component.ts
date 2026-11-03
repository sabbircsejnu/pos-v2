// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-multi-column',
  imports: [CommonModule, SharedModule],
  templateUrl: './multi-column.component.html',
  styleUrls: ['./multi-column.component.scss']
})
export class MultiColumnComponent {}
