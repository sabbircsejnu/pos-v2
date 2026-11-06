const STYLE_ID = 'receipt-print-page';
const FALLBACK_ROLL_HEIGHT_MM = 297;
const FEED_ALLOWANCE_MM = 2;
const MM_PER_CSS_PIXEL = 25.4 / 96;

/** CSS has no "80mm wide, as tall as the content" page size, so the roll height is measured from the rendered receipt. */
function thermalPageRule(): string {
  const height = document.querySelector('#receipt-print .thermal-receipt')?.getBoundingClientRect().height ?? 0;
  const heightMm = height > 0 ? Math.ceil(height * MM_PER_CSS_PIXEL) + FEED_ALLOWANCE_MM : FALLBACK_ROLL_HEIGHT_MM;
  return `@page { size: 80mm ${heightMm}mm; margin: 0; }`;
}

export function clearReceiptPrintPage(): void {
  document.getElementById(STYLE_ID)?.remove();
}

/** Sets the page size for the next print only, so other printable screens keep their own page setup. */
export function applyReceiptPrintPage(mode: string): void {
  clearReceiptPrintPage();
  const style = document.createElement('style');
  style.id = STYLE_ID;
  style.textContent = mode === 'A4' ? '@page { size: A4; margin: 10mm; }' : thermalPageRule();
  document.head.appendChild(style);
  window.addEventListener('afterprint', clearReceiptPrintPage, { once: true });
}
