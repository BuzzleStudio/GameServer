// src/modules/unsaved-guard.ts
// Tracks a "dirty" edit-buffer flag for the mail drawer.
// No DOM deps — uses window.confirm() only when needed.

export interface UnsavedGuard {
  markDirty(): void
  clearDirty(): void
  isDirty(): boolean
  /**
   * Returns true when it is safe to navigate away.
   * If dirty, prompts the user with window.confirm().
   */
  confirmNavigate(message?: string): boolean
}

export function createUnsavedGuard(): UnsavedGuard {
  let _dirty = false
  return {
    markDirty() { _dirty = true },
    clearDirty() { _dirty = false },
    isDirty() { return _dirty },
    confirmNavigate(msg = 'You have unsaved changes. Discard them?') {
      if (!_dirty) return true
      return window.confirm(msg)
    },
  }
}
