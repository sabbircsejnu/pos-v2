export interface PosTerminalOption {
  id: number;
}

/** none: block checkout; single: auto-selected; multiple: the cashier must choose explicitly. */
export type PosTerminalAvailability = 'none' | 'single' | 'multiple';

export interface PosTerminalSelection {
  availability: PosTerminalAvailability;
  selectedId: number | null;
}

/**
 * A terminal is selected automatically only when the outlet has exactly one active terminal. With several there is no
 * default (`isDefault` is ignored): a previous explicit choice is kept only while that terminal is still available.
 */
export function resolveTerminalSelection(
  terminals: readonly PosTerminalOption[],
  currentSelectedId: number | null
): PosTerminalSelection {
  if (terminals.length === 0) {
    return { availability: 'none', selectedId: null };
  }

  if (terminals.length === 1) {
    return { availability: 'single', selectedId: terminals[0].id };
  }

  const stillAvailable = terminals.some(terminal => terminal.id === currentSelectedId);
  return { availability: 'multiple', selectedId: stillAvailable ? currentSelectedId : null };
}
