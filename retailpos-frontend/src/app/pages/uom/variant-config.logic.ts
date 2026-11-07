import { SellingOption, UomConversion, UomUnit, VariantUomConfigInput, VariantUomConfigView } from '../../models/uom.model';
import { fitsPrecision } from '../../services/uom.service';

/** Form-side shape of a variant's unit configuration. The server validates and stores the real values. */
export interface ComponentValue {
  componentVariantId: number;
  name: string;
  quantity: number | null;
  isRequired: boolean;
}

export interface PurchaseValue {
  code: string;
  label: string;
  unitId: number | null;
  /** The size of one package in base units. Only a packaging unit uses it; physical units take the standard conversion. */
  size: number | null;
  factorDenominator: number;
  isDefault: boolean;
  isSealedPack: boolean;
}

export interface SellingValue {
  code: string;
  label: string;
  unitId: number | null;
  baseQuantity: number | null;
  price: number | null;
  isDefault: boolean;
  sealedPackCode: string;
  isPreparedEligible: boolean;
  components: ComponentValue[];
}

export interface ConfigValue {
  baseUnitId: number | null;
  packStateTrackingEnabled: boolean;
  repackingEnabled: boolean;
  purchaseOptions: PurchaseValue[];
  packs: SellingValue[];
  customs: SellingValue[];
}

function toSellingValue(option: SellingOption): SellingValue {
  return {
    code: option.code,
    label: option.label,
    unitId: option.unit?.id ?? null,
    baseQuantity: option.baseQuantity,
    price: option.price,
    isDefault: option.isDefault,
    sealedPackCode: option.sealedPackCode ?? '',
    isPreparedEligible: option.isPreparedEligible,
    components: option.components.map(component => ({
      componentVariantId: component.variantId,
      name: `Item #${component.variantId}`,
      quantity: component.quantity,
      isRequired: component.isRequired,
    })),
  };
}

/** A variant without a configuration starts from plain pcs: no options and no tracking. */
export function toConfigValue(view: VariantUomConfigView, units: readonly UomUnit[]): ConfigValue {
  const current = view.current;
  if (!current) {
    return {
      baseUnitId: units.find(unit => unit.code.toLowerCase() === 'pcs')?.id ?? null,
      packStateTrackingEnabled: false,
      repackingEnabled: false,
      purchaseOptions: [],
      packs: [],
      customs: [],
    };
  }
  return {
    baseUnitId: current.baseUnit.id,
    packStateTrackingEnabled: current.packStateTrackingEnabled,
    repackingEnabled: current.repackingEnabled,
    purchaseOptions: current.purchaseOptions.map(option => ({
      code: option.code,
      label: option.label,
      unitId: option.unit.id,
      size: option.factorNumerator,
      factorDenominator: option.factorDenominator,
      isDefault: option.isDefault,
      isSealedPack: option.isSealedPack,
    })),
    packs: current.sellingOptions.filter(option => option.kind === 'pack').map(toSellingValue),
    customs: current.sellingOptions.filter(option => option.kind === 'custom').map(toSellingValue),
  };
}

function duplicates(codes: readonly string[]): string[] {
  const seen = new Set<string>();
  const repeated = new Set<string>();
  codes
    .map(code => code.trim().toLowerCase())
    .filter(Boolean)
    .forEach(code => (seen.has(code) ? repeated.add(code) : seen.add(code)));
  return [...repeated];
}

/** Instant feedback only. It mirrors the server's rules so the user sees every problem at once; the server still decides. */
export function validateConfig(value: ConfigValue, units: readonly UomUnit[]): string[] {
  const errors: string[] = [];
  const unitOf = (id: number | null) => units.find(unit => unit.id === id);
  const base = unitOf(value.baseUnitId);
  if (!base) {
    return ['Choose a base unit.'];
  }
  if (base.dimension === 'packaging') {
    errors.push('A packaging name such as jar cannot be a base unit. Choose what the stock is measured in.');
  }

  value.purchaseOptions.forEach((option, index) => {
    const name = option.label.trim() || option.code.trim() || `Purchase option ${index + 1}`;
    if (!option.code.trim()) errors.push(`${name}: enter a code.`);
    const unit = unitOf(option.unitId);
    if (!unit) {
      errors.push(`${name}: choose a unit.`);
    } else if (unit.dimension === 'packaging') {
      if (!option.size || option.size <= 0) {
        errors.push(`${name}: enter how many ${base.symbol} one ${unit.symbol} holds.`);
      } else if (option.factorDenominator === 1 && !fitsPrecision(option.size, base.quantityPrecision)) {
        errors.push(`${name}: the size must fit ${base.symbol} precision (${base.quantityPrecision} decimal places).`);
      }
    } else if (unit.dimension !== base.dimension) {
      errors.push(`${name}: ${unit.symbol} has no standard conversion to ${base.symbol}.`);
    }
    if (option.isSealedPack && !value.packStateTrackingEnabled) errors.push(`${name}: a sealed pack needs pack tracking.`);
  });
  if (value.purchaseOptions.length > 0 && value.purchaseOptions.filter(option => option.isDefault).length !== 1) {
    errors.push('Choose exactly one default purchase unit.');
  }
  duplicates(value.purchaseOptions.map(option => option.code))
    .forEach(code => errors.push(`The purchase unit code '${code}' is used twice.`));

  const sealedCodes = new Set(value.purchaseOptions.filter(option => option.isSealedPack).map(option => option.code.trim()));
  const checkSelling = (option: SellingValue, name: string) => {
    if (!option.code.trim()) errors.push(`${name}: enter a code.`);
    if (option.price === null || option.price < 0) {
      errors.push(`${name}: enter a price of zero or more.`);
    } else if (!fitsPrecision(option.price, 2)) {
      errors.push(`${name}: the price cannot have more than 2 decimal places.`);
    }
    option.components.forEach(component => {
      if (!component.quantity || component.quantity <= 0) {
        errors.push(`${name}: the packaging quantity of ${component.name} must be greater than zero.`);
      }
    });
  };

  value.packs.forEach((option, index) => {
    const name = option.label.trim() || option.code.trim() || `Selling pack ${index + 1}`;
    checkSelling(option, name);
    if (!option.baseQuantity || option.baseQuantity <= 0) {
      errors.push(`${name}: enter a quantity greater than zero.`);
    } else if (!fitsPrecision(option.baseQuantity, base.quantityPrecision)) {
      errors.push(`${name}: the quantity must fit ${base.symbol} precision (${base.quantityPrecision} decimal places).`);
    }
    if (option.sealedPackCode.trim() && !sealedCodes.has(option.sealedPackCode.trim())) {
      errors.push(`${name}: '${option.sealedPackCode}' is not a sealed purchase unit of this item.`);
    }
    if (option.isPreparedEligible && !value.packStateTrackingEnabled) errors.push(`${name}: a prepared pack needs pack tracking.`);
  });

  value.customs.forEach((option, index) => {
    const name = option.label.trim() || option.code.trim() || `Custom quantity ${index + 1}`;
    checkSelling(option, name);
    const unit = unitOf(option.unitId);
    if (!unit) {
      errors.push(`${name}: choose a unit.`);
    } else if (unit.dimension !== base.dimension || unit.dimension === 'packaging') {
      errors.push(`${name}: ${unit.symbol} has no standard conversion to ${base.symbol}.`);
    }
  });

  const selling = [...value.packs, ...value.customs];
  duplicates(selling.map(option => option.code))
    .forEach(code => errors.push(`The selling option code '${code}' is used twice.`));
  if (selling.filter(option => option.isDefault).length > 1) errors.push('Choose at most one default selling option.');
  if (value.repackingEnabled && (!value.packStateTrackingEnabled || !value.packs.some(option => option.isPreparedEligible))) {
    errors.push('Repacking needs pack tracking and at least one pack that can be prepared.');
  }
  if (base.code.toLowerCase() !== 'pcs' && selling.length === 0) {
    errors.push('An item measured in a unit other than pcs needs at least one selling option.');
  }
  return errors;
}

export function toConfigInput(value: ConfigValue): VariantUomConfigInput {
  const components = (option: SellingValue) => option.components.map(component => ({
    componentVariantId: component.componentVariantId,
    quantity: component.quantity ?? 0,
    isRequired: component.isRequired,
  }));
  return {
    baseUnitId: value.baseUnitId ?? 0,
    packStateTrackingEnabled: value.packStateTrackingEnabled,
    repackingEnabled: value.repackingEnabled,
    purchaseOptions: value.purchaseOptions.map(option => ({
      code: option.code.trim(),
      unitId: option.unitId ?? 0,
      label: option.label.trim() || option.code.trim(),
      factorNumerator: option.size ?? 1,
      factorDenominator: option.factorDenominator,
      isDefault: option.isDefault,
      isSealedPack: option.isSealedPack,
    })),
    sellingOptions: [
      ...value.packs.map(option => ({
        kind: 'pack' as const,
        code: option.code.trim(),
        label: option.label.trim() || option.code.trim(),
        unitId: null,
        baseQuantity: option.baseQuantity,
        price: option.price ?? 0,
        isDefault: option.isDefault,
        sealedPackCode: option.sealedPackCode.trim() || null,
        isPreparedEligible: option.isPreparedEligible,
        components: components(option),
      })),
      ...value.customs.map(option => ({
        kind: 'custom' as const,
        code: option.code.trim(),
        label: option.label.trim() || option.code.trim(),
        unitId: option.unitId,
        baseQuantity: null,
        price: option.price ?? 0,
        isDefault: option.isDefault,
        sealedPackCode: null,
        isPreparedEligible: false,
        components: components(option),
      })),
    ],
  };
}

/** Base units per one `unit` from the standard conversions, for a hint. Null when there is no standard conversion. */
export function standardFactor(unit: UomUnit, base: UomUnit, conversions: readonly UomConversion[]): number | null {
  if (unit.id === base.id) return 1;
  const direct = conversions.find(row => row.fromUnitId === unit.id && row.toUnitId === base.id);
  if (direct) return direct.numerator / direct.denominator;
  const inverse = conversions.find(row => row.fromUnitId === base.id && row.toUnitId === unit.id);
  return inverse ? inverse.denominator / inverse.numerator : null;
}

export function suggestOptionCode(label: string): string {
  return label.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40);
}
