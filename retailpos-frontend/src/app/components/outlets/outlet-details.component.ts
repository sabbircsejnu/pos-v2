import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';

@Component({
  selector: 'app-outlet-details',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './outlet-details.component.html',
  styleUrls: ['./outlet-details.component.css']
})
export class OutletDetailsComponent implements OnInit {
  outlet = signal<Outlet | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);

  constructor(
    private outletService: OutletService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadOutlet(+id);
    }
  }

  loadOutlet(id: number): void {
    this.isLoading.set(true);
    this.outletService.getOutletById(id).subscribe({
      next: (response: any) => {
        this.outlet.set(response.data);
        this.isLoading.set(false);
      },
      error: (error: any) => {
        console.error('Error loading outlet:', error);
        this.error.set('Failed to load outlet details');
        this.isLoading.set(false);
      }
    });
  }

  editOutlet(): void {
    const outlet = this.outlet();
    if (outlet) {
      this.router.navigate(['/outlets/edit', outlet.id]);
    }
  }

  backToList(): void {
    this.router.navigate(['/outlets']);
  }
}
