import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  HrOperationalLocation,
  HrOrganisationService
} from '../../../services/hr-organisation.service';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-operational-locations-page',
  standalone: true,
  imports: [...UI_KIT],
  template: `
    <main class="container mx-auto px-4 py-3">
      <app-ui-page-header
        title="Operational Locations"
        description="Outlet and Warehouse records are read-only and owned by their source modules."
        [breadcrumbs]="breadcrumbs" />

      <app-ui-data-table
        ariaLabel="Operational locations"
        density="compact"
        rowKey="id"
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [error]="error()"
        [retryable]="false"
        errorTitle="Could not load operational locations"
        loadingLabel="Loading operational locations..."
        emptyTitle="No operational locations found."
        searchLabel="Search operational locations"
        searchPlaceholder="Search type, name, address…"
        [initialPageSize]="25" />
    </main>
  `
})
export class HrOperationalLocationsPageComponent implements OnInit {
  private readonly service = inject(HrOrganisationService);
  readonly locations = signal<HrOperationalLocation[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: 'Operational Locations' }];
  readonly columns: UiTableColumn[] = [
    { key: 'sourceEntityType', header: 'Type', sortable: true },
    { key: 'name', header: 'Name', sortable: true },
    { key: 'address', header: 'Address' },
    { key: 'sourceVersion', header: 'Source version', sortable: true },
    {
      key: 'status', header: 'Status', type: 'status', sortable: true,
      statusMap: {
        Active: { label: 'Active', variant: 'success' },
        Inactive: { label: 'Inactive', variant: 'neutral' },
        Deleted: { label: 'Deleted', variant: 'danger' }
      }
    }
  ];
  readonly rows = computed(() => this.locations().map(location => ({
    ...location,
    status: location.isSourceDeleted ? 'Deleted' : (location.isSourceActive ? 'Active' : 'Inactive')
  })));

  ngOnInit(): void {
    this.service.getOperationalLocations().subscribe({
      next: locations => {
        this.locations.set(locations);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load operational locations.');
        this.loading.set(false);
      }
    });
  }
}
