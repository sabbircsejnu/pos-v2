import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface DeviceData {
  deviceCode: string; serialNumber: string; vendorCode: string; model: string; operationalLocationProjectionId: number;
  protocolType: string; deviceTimeZoneId: string; status: string; connectionHost: string | null; connectionPort: number | null;
  firmwareVersion: string | null; credentialSecretReference: string | null;
}
export interface DeviceRow extends Omit<DeviceData, 'credentialSecretReference'> {
  id: number; businessId: number; version: number; hasCredentialReference: boolean; lastSeenAtUtc: string | null;
}
export interface DeviceMappingData { employeeId: number; deviceUserId: string; effectiveFromUtc: string; effectiveToUtc: string | null; }
export interface DeviceMappingRow extends DeviceMappingData {
  id: number; businessId: number; deviceId: number; isActive: boolean; version: number;
}
@Injectable({ providedIn: 'root' })
export class HrDeviceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1/attendance`;
  list() { return this.http.get<DeviceRow[]>(`${this.base}/devices`); }
  save(data: DeviceData, row: DeviceRow | null, replaceCredentialReference: boolean) {
    return row ? this.http.put<DeviceRow>(`${this.base}/devices/${row.id}`, { expectedVersion: row.version, data, replaceCredentialReference })
      : this.http.post<DeviceRow>(`${this.base}/devices`, data);
  }
  deactivate(row: DeviceRow) { return this.http.post<DeviceRow>(`${this.base}/devices/${row.id}/deactivate`, { expectedVersion: row.version }); }
  mappings(deviceId: number) { return this.http.get<DeviceMappingRow[]>(`${this.base}/devices/${deviceId}/mappings`); }
  map(deviceId: number, data: DeviceMappingData) { return this.http.post<DeviceMappingRow>(`${this.base}/devices/${deviceId}/mappings`, data); }
  close(row: DeviceMappingRow, effectiveToUtc: string) {
    return this.http.request<DeviceMappingRow>('DELETE', `${this.base}/device-user-mappings/${row.id}`,
      { body: { expectedVersion: row.version, effectiveToUtc } });
  }
}
