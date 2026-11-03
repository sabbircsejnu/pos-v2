import { resolveTerminalSelection } from './pos-terminals';

describe('resolveTerminalSelection', () => {
  it('blocks checkout when the outlet has no active terminal', () => {
    expect(resolveTerminalSelection([], 5)).toEqual({ availability: 'none', selectedId: null });
  });

  it('auto-selects the only active terminal', () => {
    expect(resolveTerminalSelection([{ id: 7 }], null)).toEqual({ availability: 'single', selectedId: 7 });
  });

  it('requires an explicit choice when several terminals exist, ignoring any default flag', () => {
    const terminals = [{ id: 1, isDefault: true }, { id: 2, isDefault: false }];
    expect(resolveTerminalSelection(terminals, null)).toEqual({ availability: 'multiple', selectedId: null });
  });

  it('keeps an explicit choice only while that terminal is still available', () => {
    expect(resolveTerminalSelection([{ id: 1 }, { id: 2 }], 2).selectedId).toBe(2);
    expect(resolveTerminalSelection([{ id: 1 }, { id: 3 }], 2).selectedId).toBeNull();
  });
});
