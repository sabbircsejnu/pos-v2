import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PayrollExportService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1/runs`;

  csv(runId: number) {
    return this.http.get(
      `${this.base}/${runId}/exports/csv`,
      { responseType: 'blob' }
    );
  }

  excel(runId: number) {
    return this.http.get(
      `${this.base}/${runId}/exports/excel`,
      { responseType: 'blob' }
    );
  }

  pdfSummary(runId: number) {
    return this.http.get(
      `${this.base}/${runId}/exports/pdf-summary`,
      { responseType: 'blob' }
    );
  }
}
