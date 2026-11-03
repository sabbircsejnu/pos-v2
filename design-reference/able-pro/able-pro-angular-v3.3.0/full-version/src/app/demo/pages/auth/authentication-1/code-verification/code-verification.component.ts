// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-code-verification',
  imports: [CommonModule, SharedModule],
  templateUrl: './code-verification.component.html',
  styleUrls: ['./code-verification.component.scss', '../authentication-1.scss', '../../authentication.scss']
})
export class CodeVerificationComponent {}
