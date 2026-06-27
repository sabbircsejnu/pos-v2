import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  BarcodeTemplate,
  UpsertBarcodeTemplateField,
  UpsertBarcodeTemplateRequest,
} from '../../models/barcode.model';
import { BarcodeService } from '../../services/barcode.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';

interface FieldOption {
  key: string;
  label: string;
}

@Component({
  selector: 'app-barcode-templates',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './barcode-templates.component.html',
  styleUrls: ['./barcode-templates.component.css']
})
export class BarcodeTemplatesComponent implements OnInit {
  readonly availableFields: FieldOption[] = [
    { key: 'product_name', label: 'Product Name' },
    { key: 'variant_name', label: 'Variant Name' },
    { key: 'variant_attributes', label: 'Variant Attributes' },
    { key: 'variant_sku', label: 'Variant SKU' },
    { key: 'barcode', label: 'Barcode' },
    { key: 'selling_price', label: 'Selling Price' },
    { key: 'company_name', label: 'Company Name' },
    { key: 'company_logo', label: 'Company Logo' },
  ];

  templates = signal<BarcodeTemplate[]>([]);
  isLoading = signal(false);
  showModal = signal(false);
  editingTemplateId = signal<number | null>(null);

  form = signal<UpsertBarcodeTemplateRequest>(this.buildDefaultForm());

  constructor(
    private readonly barcodeService: BarcodeService,
    private readonly alertService: AlertService,
    private readonly errorHandler: ErrorHandlerService,
    private readonly authService: AuthService,
  ) {}

  ngOnInit(): void {
    this.loadTemplates();
  }

  can(permission: string): boolean {
    return this.authService.hasPermission(permission);
  }

  loadTemplates(): void {
    this.isLoading.set(true);
    this.barcodeService.getTemplates(true).subscribe({
      next: rows => {
        this.templates.set(rows);
        this.isLoading.set(false);
      },
      error: error => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(error));
      }
    });
  }

  openCreate(): void {
    this.editingTemplateId.set(null);
    this.form.set(this.buildDefaultForm());
    this.showModal.set(true);
  }

  openEdit(template: BarcodeTemplate): void {
    this.editingTemplateId.set(template.id);

    const mappedFields: UpsertBarcodeTemplateField[] = this.availableFields.map((field, index) => {
      const existing = template.fields.find(f => f.fieldKey === field.key);
      return {
        fieldKey: field.key,
        isEnabled: existing?.isEnabled ?? (field.key !== 'company_logo'),
        sortOrder: existing?.sortOrder ?? index,
        x: existing?.x,
        y: existing?.y,
        width: existing?.width,
        height: existing?.height,
        fontSize: existing?.fontSize,
        fontWeight: existing?.fontWeight,
        align: existing?.align,
      };
    });

    this.form.set({
      name: template.name,
      templateType: template.templateType,
      paperType: template.paperType,
      labelWidthMm: Number(template.labelWidthMm),
      labelHeightMm: Number(template.labelHeightMm),
      isDefault: template.isDefault,
      isActive: template.isActive,
      fields: mappedFields,
    });

    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  saveTemplate(): void {
    const payload = this.form();

    if (!payload.name.trim()) {
      this.alertService.warning('Template name is required.');
      return;
    }

    if (payload.labelWidthMm <= 0 || payload.labelHeightMm <= 0) {
      this.alertService.warning('Label width and height must be greater than 0.');
      return;
    }

    const templateId = this.editingTemplateId();
    const request = templateId
      ? this.barcodeService.updateTemplate(templateId, payload)
      : this.barcodeService.createTemplate(payload);

    request.subscribe({
      next: () => {
        this.alertService.success(templateId ? 'Template updated successfully.' : 'Template created successfully.');
        this.closeModal();
        this.loadTemplates();
      },
      error: error => this.alertService.error(this.errorHandler.extractErrorMessage(error))
    });
  }

  deleteTemplate(template: BarcodeTemplate): void {
    this.alertService.confirm(
      `Delete template \"${template.name}\"?`,
      () => {
        this.barcodeService.deleteTemplate(template.id).subscribe({
          next: () => {
            this.alertService.success('Template deleted successfully.');
            this.loadTemplates();
          },
          error: error => this.alertService.error(this.errorHandler.extractErrorMessage(error))
        });
      },
      'Delete Template',
      'Delete',
      'Cancel'
    );
  }

  setDefault(template: BarcodeTemplate): void {
    this.barcodeService.setDefaultTemplate(template.id).subscribe({
      next: () => {
        this.alertService.success('Template set as default.');
        this.loadTemplates();
      },
      error: error => this.alertService.error(this.errorHandler.extractErrorMessage(error))
    });
  }

  updateFieldEnabled(fieldKey: string, enabled: boolean): void {
    const form = this.form();
    const fields = form.fields.map(field => field.fieldKey === fieldKey ? { ...field, isEnabled: enabled } : field);
    this.form.set({ ...form, fields });
  }

  isFieldEnabled(fieldKey: string): boolean {
    const field = this.form().fields.find(x => x.fieldKey === fieldKey);
    return field?.isEnabled ?? false;
  }

  getEnabledFieldCount(template: BarcodeTemplate): number {
    return template.fields.filter(field => field.isEnabled).length;
  }

  private buildDefaultForm(): UpsertBarcodeTemplateRequest {
    return {
      name: '',
      templateType: 'retail',
      paperType: 'label',
      labelWidthMm: 60,
      labelHeightMm: 40,
      isDefault: false,
      isActive: true,
      fields: this.availableFields.map((field, index) => ({
        fieldKey: field.key,
        isEnabled: field.key !== 'company_logo',
        sortOrder: index,
      }))
    };
  }
}
