/**
 * Readable variant attributes ("Colour: Black | Size: M") from the stored attribute JSON, shared by the POS product
 * search and the Exchange replacement search. Accepts an array of attribute rows, a key/value object or legacy plain
 * text; inactive or empty attributes are left out.
 */
export function formatVariantAttributes(raw?: string | null): string {
  if (!raw || !raw.trim()) {
    return '';
  }

  try {
    const parsed: unknown = JSON.parse(raw);

    if (Array.isArray(parsed)) {
      return parsed
        .filter(row => isAttributeRowActive(row))
        .map((row: Record<string, unknown>) => {
          const label = humanizeLabel(String(row['displayName'] || row['attributeName'] || row['name'] || row['key'] || '').trim());
          const value = String(row['displayValue'] ?? row['value'] ?? '').trim();
          return label && value ? `${label}: ${value}` : '';
        })
        .filter(Boolean)
        .join(' | ');
    }

    if (parsed && typeof parsed === 'object') {
      return Object.entries(parsed as Record<string, unknown>)
        .filter(([key, value]) => isAttributeEntryActive(key, value))
        .map(([key, value]) => `${humanizeLabel(key)}: ${String(value).trim()}`)
        .join(' | ');
    }
  } catch {
    // Keep graceful fallback for older plain-text stored attributes.
  }

  return raw
    .replace(/[{}"]+/g, '')
    .replace(/\s*,\s*/g, ' | ')
    .replace(/\s*\/\s*/g, ' | ')
    .trim();
}

function isAttributeRowActive(row: unknown): boolean {
  if (!row || typeof row !== 'object') return false;
  const value = row as Record<string, unknown>;

  const status = String(value['status'] ?? '').toLowerCase();
  if (status === 'inactive' || status === 'deleted') return false;

  if (value['isDeleted'] === true || value['deleted'] === true) return false;
  if (value['isActive'] === false || value['active'] === false) return false;

  return !!String(value['displayValue'] ?? value['value'] ?? '').trim();
}

function isAttributeEntryActive(key: string, value: unknown): boolean {
  if (value === null || value === undefined) return false;

  const normalizedKey = key.trim().toLowerCase();
  if (['isactive', 'active', 'isdeleted', 'deleted', 'status'].includes(normalizedKey)) {
    return false;
  }

  const text = String(value).trim();
  if (!text) return false;

  return !['inactive', 'deleted', 'false', 'null', 'undefined'].includes(text.toLowerCase());
}

function humanizeLabel(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/^./, c => c.toUpperCase());
}
