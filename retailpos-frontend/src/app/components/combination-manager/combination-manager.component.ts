import { Component, Input, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProductVariationService, CombinationDto, ProductVariationDto } from '../../services/product-variation.service';
import { VariationService } from '../../services/variation.service';
import { Variation } from '../../models/variation.model';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

@Component({
  selector: 'app-combination-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
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
  /**
   * Tracks which options are checked for each selected variation type.
   * Key = variationId, Value = Set of selected optionIds.
   */
  selectedOptionsByVariation = signal<{ [variationId: number]: number[] }>({});
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

  private auth = inject(AuthService);
  canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));

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

    // Load assigned variations (includes selectedOptionIds per variation)
    this.productVariationService.getProductVariations(this.productId).subscribe({
      next: (variations) => {
        this.assignedVariations.set(variations);
        this.selectedVariationIds.set(variations.map(v => v.variationId));

        // Restore per-variation selected options from server state
        const optionsMap: { [variationId: number]: number[] } = {};
        for (const v of variations) {
          optionsMap[v.variationId] = v.selectedOptionIds?.length
            ? [...v.selectedOptionIds]
            : v.options.map(o => o.id); // default to all if nothing saved
        }
        this.selectedOptionsByVariation.set(optionsMap);
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

  // â”€â”€ Variation type toggle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

  toggleVariation(variationId: number): void {
    const current = this.selectedVariationIds();
    const optionsMap = { ...this.selectedOptionsByVariation() };

    if (current.includes(variationId)) {
      // Deselecting the variation type — remove it
      this.selectedVariationIds.set(current.filter(id => id !== variationId));
      delete optionsMap[variationId];
    } else {
      // Selecting the variation type for the first time on this product.
      // Pre-select options based on the variation type's autoSelectAllOptions setting:
      //   true  → pre-select all options (e.g. Size where every option is usually needed)
      //   false → start with nothing selected so the user picks only what applies (e.g. Color)
      this.selectedVariationIds.set([...current, variationId]);
      const variation = this.variationService.variations().find(v => v.id === variationId);
      if (variation?.autoSelectAllOptions) {
        optionsMap[variationId] = variation.options.map(o => o.id);
      } else {
        optionsMap[variationId] = [];
      }
    }

    this.selectedOptionsByVariation.set(optionsMap);
  }

  // â”€â”€ Option toggle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

  toggleOption(variationId: number, optionId: number): void {
    const map = { ...this.selectedOptionsByVariation() };
    const current = map[variationId] ?? [];

    map[variationId] = current.includes(optionId)
      ? current.filter(id => id !== optionId)
      : [...current, optionId];

    this.selectedOptionsByVariation.set(map);
  }

  isOptionSelected(variationId: number, optionId: number): boolean {
    return (this.selectedOptionsByVariation()[variationId] ?? []).includes(optionId);
  }

  getSelectedOptionsCount(variationId: number): number {
    return (this.selectedOptionsByVariation()[variationId] ?? []).length;
  }

  getTotalOptionsCount(variationId: number): number {
    const variation = this.variationService.variations().find(v => v.id === variationId);
    return variation?.options.length ?? 0;
  }

  /**
   * Returns true only when every selected variation type has at least one option selected.
   * Used to disable the Generate Combinations buttons.
   */
  canGenerateCombinations(): boolean {
    const selectedIds = this.selectedVariationIds();
    if (selectedIds.length === 0) return false;
    const map = this.selectedOptionsByVariation();
    return selectedIds.every(id => (map[id] ?? []).length > 0);
  }

  /**
   * Returns combinations that use options no longer selected for this product.
   * Used to warn the user before regenerating.
   */
  getInvalidCombinations(): CombinationDto[] {
    const map = this.selectedOptionsByVariation();
    const allSelectedOptions = new Set(Object.values(map).flat());
    return this.combinations().filter(combo =>
      combo.optionIds.some(oid => !allSelectedOptions.has(oid))
    );
  }

  // â”€â”€ Save variation assignment â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

  saveVariationAssignment(): void {
    // Validate: each selected variation must have at least one option selected
    const map = this.selectedOptionsByVariation();
    const invalidVariations = this.selectedVariationIds().filter(id => (map[id] ?? []).length === 0);
    if (invalidVariations.length > 0) {
      this.alertService.warning('Each selected variation type must have at least one option selected.');
      return;
    }

    // Warn if existing combinations will become invalid
    const invalid = this.getInvalidCombinations();
    if (invalid.length > 0) {
      this.alertService.confirm(
        `${invalid.length} existing combination(s) use options that are no longer selected. ` +
        `They will be kept but won't be regenerated. Do you want to continue?`,
        () => this.doSaveVariationAssignment(),
        'Save Variation Assignment'
      );
    } else {
      this.doSaveVariationAssignment();
    }
  }

  private doSaveVariationAssignment(): void {
    this.isLoading.set(true);
    this.productVariationService.assignVariations(
      this.productId,
      this.selectedVariationIds(),
      this.selectedOptionsByVariation()
    ).subscribe({
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

  // â”€â”€ Generate combinations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

  generateAllCombinations(): void {
    this.alertService.confirm(
      'This will generate all combinations using the currently selected options. Continue?',
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

  // â”€â”€ Manual combination â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

  // â”€â”€ Edit / Delete â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
