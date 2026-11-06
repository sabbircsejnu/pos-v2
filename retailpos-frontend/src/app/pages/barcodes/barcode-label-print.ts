import JsBarcode from 'jsbarcode';
import { BarcodePrintQueueItem } from '../../models/barcode.model';

/** Physical label stock and printer margins, in millimetres. */
export interface LabelPrintSettings {
  widthMm: number;
  heightMm: number;
  marginTopMm: number;
  marginRightMm: number;
  marginBottomMm: number;
  marginLeftMm: number;
}

export interface ThermalLabelText {
  company: string;
  title: string;
  variantLine: string;
}

export interface ThermalLabelContent extends ThermalLabelText {
  barcodeSvg: string;
  priceText: string;
}

export interface FittedText {
  fontMm: number;
  lines: number;
  heightMm: number;
}

export interface ThermalLabelLayout {
  contentWidthMm: number;
  contentHeightMm: number;
  company: FittedText;
  title: FittedText;
  variant: FittedText;
  price: FittedText;
  barcodeHeightMm: number;
}

export interface RenderedBarcode {
  markup: string;
  modules: number;
}

export const DEFAULT_PRINTER_MARGIN_MM = 1;
/** Two dots on a 203 dpi thermal head, the usual minimum for a reliable scan. */
export const MIN_RELIABLE_MODULE_WIDTH_MM = 0.25;

const MAX_LABEL_MM = 300;
const MAX_MARGIN_MM = 20;
const MIN_CONTENT_WIDTH_MM = 24;
const MIN_CONTENT_HEIGHT_MM = 16;
const A4_MAX_LABEL_WIDTH_MM = 194;
const A4_MAX_LABEL_HEIGHT_MM = 280;

const LINE_HEIGHT = 1.15;
const ROW_GAP_MM = 0.4;
const MIN_BARCODE_HEIGHT_MM = 6;
const MAX_BARCODE_HEIGHT_TO_WIDTH = 0.6;
const WRAP_SLACK = 1.08;
const QUIET_ZONE_MODULES = 10;
const SVG_NS = 'http://www.w3.org/2000/svg';

/** Field-keyed messages; an empty object means the settings can be printed. */
export function validateLabelSettings(settings: LabelPrintSettings, format: 'thermal' | 'a4'): Record<string, string> {
  const errors: Record<string, string> = {};
  const { widthMm, heightMm } = settings;

  if (!isWithin(widthMm, 1, MAX_LABEL_MM)) {
    errors['widthMm'] = `Enter a width up to ${MAX_LABEL_MM} mm.`;
  }
  if (!isWithin(heightMm, 1, MAX_LABEL_MM)) {
    errors['heightMm'] = `Enter a height up to ${MAX_LABEL_MM} mm.`;
  }

  if (format === 'a4') {
    if (!errors['widthMm'] && widthMm > A4_MAX_LABEL_WIDTH_MM) {
      errors['widthMm'] = `A4 labels can be at most ${A4_MAX_LABEL_WIDTH_MM} mm wide.`;
    }
    if (!errors['heightMm'] && heightMm > A4_MAX_LABEL_HEIGHT_MM) {
      errors['heightMm'] = `A4 labels can be at most ${A4_MAX_LABEL_HEIGHT_MM} mm high.`;
    }
    return errors;
  }

  const margins: [string, number][] = [
    ['marginTopMm', settings.marginTopMm],
    ['marginRightMm', settings.marginRightMm],
    ['marginBottomMm', settings.marginBottomMm],
    ['marginLeftMm', settings.marginLeftMm],
  ];
  for (const [field, value] of margins) {
    if (!isWithin(value, 0, MAX_MARGIN_MM)) {
      errors[field] = `Enter 0 to ${MAX_MARGIN_MM} mm.`;
    }
  }

  if (Object.keys(errors).length === 0) {
    if (widthMm - settings.marginLeftMm - settings.marginRightMm < MIN_CONTENT_WIDTH_MM) {
      errors['widthMm'] = `Width minus left and right margins must be at least ${MIN_CONTENT_WIDTH_MM} mm.`;
    }
    if (heightMm - settings.marginTopMm - settings.marginBottomMm < MIN_CONTENT_HEIGHT_MM) {
      errors['heightMm'] = `Height minus top and bottom margins must be at least ${MIN_CONTENT_HEIGHT_MM} mm.`;
    }
  }
  return errors;
}

/**
 * Sizes every row of a label so long text shrinks and wraps within a reserved box and the barcode box
 * is never squeezed. BarcodeLabelPdfService has a C# copy (ThermalLabelLayout.cs); both are pinned to
 * the same expected values so browser print and PDF produce identical boxes. Change them together.
 */
export function computeThermalLabelLayout(settings: LabelPrintSettings, text: ThermalLabelText): ThermalLabelLayout {
  const contentWidthMm = settings.widthMm - settings.marginLeftMm - settings.marginRightMm;
  const contentHeightMm = settings.heightMm - settings.marginTopMm - settings.marginBottomMm;
  const scaled = (factor: number, min: number, max: number) => Math.min(max, Math.max(min, contentHeightMm * factor));
  const titleLines = contentHeightMm >= 30 ? 3 : contentHeightMm >= 20 ? 2 : 1;

  const company = fitText(text.company, contentWidthMm, scaled(0.11, 2.2, 3.4), 1.9, 1);
  const title = fitText(text.title, contentWidthMm, scaled(0.115, 2.2, 3.4), 1.9, titleLines);
  let variant = fitText(text.variantLine, contentWidthMm, scaled(0.095, 2, 3), 1.7, 1);
  const priceFont = floorToTenth(scaled(0.125, 2.4, 3.8));
  const price: FittedText = { fontMm: priceFont, lines: 1, heightMm: rowHeight(priceFont, 1) };

  const available = (rows: FittedText[]) => {
    const visible = rows.filter(row => row.lines > 0);
    return contentHeightMm - visible.reduce((sum, row) => sum + row.heightMm, 0) - visible.length * ROW_GAP_MM;
  };

  // On very short labels the barcode matters more than the variant line, which is the first row to go.
  if (variant.lines > 0 && available([company, title, variant, price]) < MIN_BARCODE_HEIGHT_MM) {
    variant = { fontMm: 0, lines: 0, heightMm: 0 };
  }

  const barcodeHeightMm = Math.min(
    available([company, title, variant, price]),
    contentWidthMm * MAX_BARCODE_HEIGHT_TO_WIDTH,
  );

  return {
    contentWidthMm,
    contentHeightMm,
    company,
    title,
    variant,
    price,
    barcodeHeightMm: roundMm(barcodeHeightMm),
  };
}

/** Variant name and SKU for the label, leaving out anything that would only repeat other text. */
export function buildThermalVariantLine(item: Pick<BarcodePrintQueueItem, 'variantName' | 'variantSku' | 'productName'>): string {
  const sku = (item.variantSku || '').trim();
  const name = (item.variantName || '').trim();
  const lowered = name.toLowerCase();
  const nameApplies = !!name
    && lowered !== 'default'
    && lowered !== (item.productName || '').trim().toLowerCase()
    && lowered !== sku.toLowerCase();

  return [nameApplies ? name : '', sku].filter(Boolean).join(' | ');
}

/** Labels in print order: queue order, with each item repeated its requested quantity, up to `limit`. */
export function expandQueue(queue: BarcodePrintQueueItem[], limit = Number.POSITIVE_INFINITY): BarcodePrintQueueItem[] {
  const labels: BarcodePrintQueueItem[] = [];
  for (const row of queue) {
    for (let i = 0; i < row.quantityPrinted && labels.length < limit; i += 1) {
      labels.push(row);
    }
  }
  return labels;
}

/**
 * CODE128, as elsewhere in the module, with the quiet zone drawn inside the SVG so a scanner can find the
 * ends of the code however small the printer margins are. Throws if the value cannot be encoded.
 */
export function renderThermalBarcode(value: string): RenderedBarcode {
  const barWidth = 2;
  const svg = document.createElementNS(SVG_NS, 'svg');

  JsBarcode(svg, value, {
    format: 'CODE128',
    displayValue: false,
    margin: 0,
    marginLeft: QUIET_ZONE_MODULES * barWidth,
    marginRight: QUIET_ZONE_MODULES * barWidth,
    height: 100,
    width: barWidth,
    background: '#ffffff',
    lineColor: '#000000',
  });

  const modules = Number.parseFloat(svg.getAttribute('width') ?? '0') / barWidth - QUIET_ZONE_MODULES * 2;
  svg.setAttribute('preserveAspectRatio', 'none');
  svg.setAttribute('shape-rendering', 'crispEdges');
  svg.setAttribute('width', '100%');
  svg.setAttribute('height', '100%');
  svg.setAttribute('aria-label', `Barcode ${value}`);
  return { markup: svg.outerHTML, modules };
}

/** Printed width of one barcode module, used to warn about bars too thin for a thermal head to print cleanly. */
export function barcodeModuleWidthMm(modules: number, contentWidthMm: number): number {
  return contentWidthMm / (modules + QUIET_ZONE_MODULES * 2);
}

/** The one document used for preview and print: one physical page per label, with no page margins. */
export function buildThermalPrintHtml(labels: ThermalLabelContent[], settings: LabelPrintSettings): string {
  const pages = labels.map(label => renderLabel(label, settings)).join('');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>Barcode Labels</title>
<style>
  @page { size: ${settings.widthMm}mm ${settings.heightMm}mm; margin: 0; }
  *, *::before, *::after { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; background: #ffffff; color: #000000; }
  .label {
    width: ${settings.widthMm}mm;
    height: ${settings.heightMm}mm;
    padding: ${settings.marginTopMm}mm ${settings.marginRightMm}mm ${settings.marginBottomMm}mm ${settings.marginLeftMm}mm;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: ${ROW_GAP_MM}mm;
    overflow: hidden;
    background: #ffffff;
    font-family: Arial, Helvetica, sans-serif;
    font-weight: 700;
    text-align: center;
    break-inside: avoid;
    break-after: page;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
  .label:last-child { break-after: auto; }
  .row {
    flex: none;
    display: -webkit-box;
    -webkit-box-orient: vertical;
    overflow: hidden;
    overflow-wrap: anywhere;
    line-height: ${LINE_HEIGHT};
  }
  .barcode { flex: none; width: 100%; overflow: hidden; }
  .barcode svg { display: block; width: 100%; height: 100%; }
  @media screen {
    html { background: #e5e7eb; }
    body { padding: 3mm; background: transparent; }
    .label { margin: 0 auto 3mm; box-shadow: 0 0 0 0.2mm #6b7280; }
    .label:last-child { margin-bottom: 0; }
  }
</style>
</head>
<body>${pages}</body>
</html>`;
}

function renderLabel(label: ThermalLabelContent, settings: LabelPrintSettings): string {
  const layout = computeThermalLabelLayout(settings, label);

  return `<section class="label">${renderRow(label.company, layout.company)}${renderRow(label.title, layout.title)}${renderRow(label.variantLine, layout.variant)}`
    + `<div class="barcode" style="height:${layout.barcodeHeightMm}mm">${label.barcodeSvg}</div>`
    + `${renderRow(label.priceText, layout.price)}</section>`;
}

function renderRow(text: string, fitted: FittedText): string {
  if (fitted.lines === 0) {
    return '';
  }
  return `<div class="row" style="height:${fitted.heightMm}mm;font-size:${fitted.fontMm}mm;-webkit-line-clamp:${fitted.lines}">${escapeHtml(text)}</div>`;
}

function fitText(text: string, widthMm: number, maxFontMm: number, minFontMm: number, maxLines: number): FittedText {
  const value = text.trim();
  if (!value) {
    return { fontMm: 0, lines: 0, heightMm: 0 };
  }

  const widthEm = estimateWidthEm(value);
  const maxTenths = Math.floor(maxFontMm * 10);
  const minTenths = Math.round(minFontMm * 10);
  for (let tenths = maxTenths; tenths >= minTenths; tenths -= 1) {
    const fontMm = tenths / 10;
    const lineWidthMm = widthEm * fontMm;
    const lines = lineWidthMm <= widthMm ? 1 : Math.ceil((lineWidthMm * WRAP_SLACK) / widthMm);
    if (lines <= maxLines) {
      return { fontMm, lines, heightMm: rowHeight(fontMm, lines) };
    }
  }

  // Still too long at the smallest legible size: the text is clamped to the box with an ellipsis.
  const fontMm = minTenths / 10;
  return { fontMm, lines: maxLines, heightMm: rowHeight(fontMm, maxLines) };
}

/** Deliberately generous per-character widths for bold sans-serif text, so an estimate rarely undershoots. */
function estimateWidthEm(text: string): number {
  let em = 0;
  for (const char of text) {
    if (char === ' ') {
      em += 0.28;
    } else if (char >= 'A' && char <= 'Z') {
      em += 0.72;
    } else {
      em += 0.56;
    }
  }
  return em;
}

function rowHeight(fontMm: number, lines: number): number {
  return roundMm(fontMm * LINE_HEIGHT * lines);
}

function floorToTenth(value: number): number {
  return Math.floor(value * 10) / 10;
}

function roundMm(value: number): number {
  return Math.round(value * 100) / 100;
}

function isWithin(value: number, min: number, max: number): boolean {
  return typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max;
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
