/** Formats integer minor units using the currency's own number of decimals (JPY has none, GBP has two). */
export function formatMoney(amountMinor: number, currency: string): string {
  const formatter = new Intl.NumberFormat(undefined, { style: 'currency', currency })
  const digits = formatter.resolvedOptions().maximumFractionDigits ?? 2
  return formatter.format(amountMinor / 10 ** digits)
}

/** Parses a user-typed major amount ("12.50") into minor units for the currency, or null if invalid. */
export function toMinor(amount: string, currency: string): number | null {
  const value = Number(amount)
  if (!Number.isFinite(value) || value <= 0) {
    return null
  }
  const digits = new Intl.NumberFormat(undefined, { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2
  return Math.round(value * 10 ** digits)
}
