import { Component, Input, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProductVariationService, CombinationDto, ProductVariationDto } from '../../services/product-variation.service';
import { VariationService } from '../../services/variation.service';
import { Variation } from '../../models/variation.model';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

@Component({
  selector: 'app-combination-manager',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './combination-manager.component.html',
  styleUrls: ['./combination-manager.component.css']
})
export class CombinationManagerComponent implements OnInit {
  @Input() productId!: number;
  @Input() productName!: string;
  @Input() basePrice!: number;

  availableVariations = signal<Variation[]>([]);
  assignedVariations = signal<ProductVariationDto[]>([]);
  selectedVariationIds = signal<number[]>([]);
  combinations = signal<CombinationDto[]>([]);
  
  showManualModal = signal(false);
  showEditModal = signal(false);
  isLoading = signal(false);
  editingCombination = signal<CombinationDto | null>(null);

  // Manual combination form
  manualForm = signal({
    sku: '',
    barcode: '',
    priceAdjustment: 0,
    costAdjustment: 0,
    selectedOptions: {} as { [variationId: number]: number }
  });

  constructor(
    private productVariationService: ProductVariationService,
    public variationService: VariationService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.isLoading.set(true);
    this.variationService.loadVariations();
    
    // Load assigned variations
    this.productVariationService.getProductVariations(this.productId).subscribe({
      next: (variations) => {
        this.assignedVariations.set(variations);
        this.selectedVariationIds.set(variations.map(v => v.variationId));
      },
      error: () => {
        this.assignedVariations.set([]);
      }
    });

    // Load existing combinations
    this.productVariationService.getCombinations(this.productId).subscribe({
      next: (combos) => {
        this.combinations.set(combos);
        this.isLoading.set(false);
      },
      error: () => {
        this.combinations.set([]);
        this.isLoading.set(false);
      }
    });
  }

  get unassignedVariations(): Variation[] {
    const assigned = this.selectedVariationIds();
    return this.variationService.variations().filter(v => !assigned.includes(v.id));
  }

  toggleVariation(variationId: number): void {
    const current = this.selectedVariationIds();
    if (current.includes(variationId)) {
      this.selectedVariationIds.set(current.filter(id => id !== variationId));
    } else {
      this.selectedVariationIds.set([...current, variationId]);
    }
  }

  saveVariationAssignment(): void {
    this.isLoading.set(true);
    this.productVariationService.assignVariations(this.productId, this.selectedVariationIds()).subscribe({
      next: () => {
        this.alertService.success('Variations assigned successfully');
        this.loadData();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  generateAllCombinations(): void {
    this.alertService.confirm(
      'This will generate all possible combinations. Continue?',
      () => {
        this.isLoading.set(true);
        this.productVariationService.generateAllCombinations(this.productId).subscribe({
          next: (response) => {
            this.alertService.success(response.message);
            this.loadData();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
            this.isLoading.set(false);
          }
        });
      },
      'Generate All Combinations'
    );
  }

  generateSelectedCombinations(): void {
    const selected = this.selectedVariationIds();
    if (selected.length === 0) {
      this.alertService.warning('Please select at least one variation');
      return;
    }

    this.isLoading.set(true);
    this.productVariationService.generateSelectedCombinations(this.productId, selected).subscribe({
      next: (response) => {
        this.alertService.success(response.message);
        this.loadData();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  openManualModal(): void {
    this.manualForm.set({
      sku: '',
      barcode: '',
      priceAdjustment: 0,
      costAdjustment: 0,
      selectedOptions: {}
    });
    this.showManualModal.set(true);
  }

  createManualCombination(): void {
    const form = this.manualForm();
    const optionIds = Object.values(form.selectedOptions).filter(id => id > 0);

    if (optionIds.length === 0) {
      this.alertService.warning('Please select at least one option');
      return;
    }

    this.productVariationService.createManualCombination(this.productId, {
      sku: form.sku || undefined,
      barcode: form.barcode || undefined,
      priceAdjustment: form.priceAdjustment,
      costAdjustment: form.costAdjustment,
      optionIds
    }).subscribe({
      next: () => {
        this.alertService.success('Combination created successfully');
        this.showManualModal.set(false);
        this.loadData();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  openEditModal(combination: CombinationDto): void {
    this.editingCombination.set(combination);
    this.showEditModal.set(true);
  }

  updateCombination(): void {
    const combo = this.editingCombination();
    if (!combo) return;

    this.productVariationService.updateCombination(this.productId, combo.id!, {
      sku: combo.sku,
      barcode: combo.barcode,
      priceAdjustment: combo.priceAdjustment,
      costAdjustment: combo.costAdjustment
    }).subscribe({
      next: () => {
        this.alertService.success('Combination updated successfully');
        this.showEditModal.set(false);
        this.loadData();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  deleteCombination(combination: CombinationDto): void {
    this.alertService.confirm(
      `Are you sure you want to delete combination "${combination.combinationName}"?`,
      () => {
        this.productVariationService.deleteCombination(this.productId, combination.id!).subscribe({
          next: () => {
            this.alertService.success('Combination deleted successfully');
            this.loadData();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Delete Combination',
      'Delete',
      'Cancel'
    );
  }
}
