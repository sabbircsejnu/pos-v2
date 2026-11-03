import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-accounting-placeholder',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './accounting-placeholder.component.html',
  styleUrls: ['./accounting-placeholder.component.css']
})
export class AccountingPlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  title = (this.route.snapshot.data['title'] as string) ?? 'Accounting';
}
