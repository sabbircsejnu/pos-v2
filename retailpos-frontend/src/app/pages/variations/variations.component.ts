import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { VariationService } from '../../services/variation.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  Variation,
  CreateVariationDto,
  CreateVariationOptionDto,
  UpdateVariationDto,
  UpdateVariationOptionDto
} from '../../models/variation.model';

@Component({
  selector: 'app-variations',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './variations.component.html',
  styleUrls: ['./variations.component.css']
})
export class VariationsComponent implements OnInit {
  showModal = signal(false);
  showOptionModal = signal(false);
  isEditMode = signal(false);
  isEditOption = signal(false);
  selectedVariationId = signal<number | null>(null);
  selectedOptionId = signal<number | null>(null);

  // Form data
  variationForm = signal<CreateVariationDto>({
    name: '',
    displayOrder: 0,
    isActive: true,
    options: []
  });

  optionForm = signal<CreateVariationOptionDto>({
    name: '',
    priceAdjustment: 0,
    displayOrder: 0,
    isActive: true
  });

  // Temporary option list while creating variation
  tempOptions = signal<CreateVariationOptionDto[]>([]);

  constructor(
    public variationService: VariationService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.variationService.loadVariations();
  }

  // Open modal to create new variation
  openCreateModal(): void {
    this.isEditMode.set(false);
    this.variationForm.set({
      name: '',
      displayOrder: 0,
      isActive: true,
      options: []
    });
    this.tempOptions.set([]);
    this.showModal.set(true);
  }

  // Open modal to edit variation
  openEditModal(variation: Variation): void {
    this.isEditMode.set(true);
    this.selectedVariationId.set(variation.id);
    this.variationForm.set({
      name: variation.name,
      displayOrder: variation.displayOrder,
      isActive: variation.isActive,
      options: []
    });
    this.showModal.set(true);
  }

  // Close variation modal
  closeModal(): void {
    this.showModal.set(false);
    this.tempOptions.set([]);
  }

  // Add option to temporary list (only when creating variation)
  addOptionToList(): void {
    const option = this.optionForm();
    if (option.name.trim()) {
      this.tempOptions.update(opts => [...opts, { ...option }]);
      this.optionForm.set({
        name: '',
        priceAdjustment: 0,
        displayOrder: this.tempOptions().length,
        isActive: true
      });
    }
  }

  // Remove option from temporary list
  removeOptionFromList(index: number): void {
    this.tempOptions.update(opts => opts.filter((_, i) => i !== index));
  }

  // Save variation (create or update)
  saveVariation(): void {
    const form = this.variationForm();
    
    if (!form.name.trim()) {
      this.alertService.warning('Variation name is required');
      return;
    }

    if (this.isEditMode()) {
      // Update existing variation
      const updateDto: UpdateVariationDto = {
        name: form.name,
        displayOrder: form.displayOrder,
        isActive: form.isActive
      };

      this.variationService.updateVariation(this.selectedVariationId()!, updateDto).subscribe({
        next: () => {
          this.alertService.success('Variation updated successfully');
          this.variationService.loadVariations();
          this.closeModal();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
        }
      });
    } else {
      // Create new variation
      const createDto: CreateVariationDto = {
        name: form.name,
        displayOrder: form.displayOrder,
        isActive: form.isActive,
        options: this.tempOptions()
      };

      if (createDto.options.length === 0) {
        this.alertService.warning('Please add at least one option');
        return;
      }

      this.variationService.createVariation(createDto).subscribe({
        next: () => {
          this.alertService.success('Variation created successfully');
          this.variationService.loadVariations();
          this.closeModal();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
        }
      });
    }
  }

  // Delete variation
  deleteVariation(id: number, name: string): void {
    this.alertService.confirm(
      `Are you sure you want to delete "${name}"?`,
      () => {
        this.variationService.deleteVariation(id).subscribe({
          next: (message) => {
            this.alertService.success(message);
            this.variationService.loadVariations();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Delete Variation',
      'Delete',
      'Cancel'
    );
  }

  // Open modal to add option to existing variation
  openAddOptionModal(variationId: number): void {
    this.selectedVariationId.set(variationId);
    this.isEditOption.set(false);
    this.optionForm.set({
      name: '',
      priceAdjustment: 0,
      displayOrder: 0,
      isActive: true
    });
    this.showOptionModal.set(true);
  }

  // Open modal to edit existing option
  openEditOptionModal(variationId: number, optionId: number, optionName: string, priceAdj: number, displayOrder: number, isActive: boolean): void {
    this.selectedVariationId.set(variationId);
    this.selectedOptionId.set(optionId);
    this.isEditOption.set(true);
    this.optionForm.set({
      name: optionName,
      priceAdjustment: priceAdj,
      displayOrder: displayOrder,
      isActive: isActive
    });
    this.showOptionModal.set(true);
  }

  // Close option modal
  closeOptionModal(): void {
    this.showOptionModal.set(false);
  }

  // Save option (create or update for existing variation)
  saveOption(): void {
    const form = this.optionForm();

    if (!form.name.trim()) {
      this.alertService.warning('Option name is required');
      return;
    }

    if (this.isEditOption()) {
      // Update existing option
      const updateDto: UpdateVariationOptionDto = {
        name: form.name,
        priceAdjustment: form.priceAdjustment,
        displayOrder: form.displayOrder,
        isActive: form.isActive
      };

      this.variationService.updateOption(this.selectedOptionId()!, updateDto).subscribe({
        next: () => {
          this.alertService.success('Option updated successfully');
          this.variationService.loadVariations();
          this.closeOptionModal();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
        }
      });
    } else {
      // Create new option
      this.variationService.createOption(this.selectedVariationId()!, form).subscribe({
        next: () => {
          this.alertService.success('Option created successfully');
          this.variationService.loadVariations();
          this.closeOptionModal();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
        }
      });
    }
  }

  // Delete option
  deleteOption(optionId: number, optionName: string): void {
    this.alertService.confirm(
      `Are you sure you want to delete option "${optionName}"?`,
      () => {
        this.variationService.deleteOption(optionId).subscribe({
          next: (message) => {
            this.alertService.success(message);
            this.variationService.loadVariations();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Delete Option',
      'Delete',
      'Cancel'
    );
  }

  // Track by for ngFor
  trackByVariationId(index: number, variation: Variation): number {
    return variation.id;
  }

  trackByOptionId(index: number, option: any): number {
    return option.id || index;
  }
}
