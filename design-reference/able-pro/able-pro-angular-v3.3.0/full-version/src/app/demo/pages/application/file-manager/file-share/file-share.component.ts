// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-file-share',
  imports: [CommonModule, SharedModule],
  templateUrl: './file-share.component.html',
  styleUrls: ['./file-share.component.scss']
})
export class FileShareComponent {}
