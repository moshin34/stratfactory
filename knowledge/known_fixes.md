# Known Fixes (paste the relevant bullets into fix prompts)

- `CS0246: The type or namespace name 'Display' could not be found` → Add `using System.ComponentModel.DataAnnotations;`
- `CS0103: The name 'ATR' does not exist` → Add `using NinjaTrader.NinjaScript.Indicators;`
- `SetStopLoss` overload mismatch → Use `SetStopLoss(entrySignal, CalculationMode.Price, price, false)`
- One class per file; class name must equal file name; namespace must be `Standalone.Strategies`
- Attach partial exits to entry signal: `ExitLongLimit(qty, price, "TP1", "ENTRY")` / `ExitShortLimit(...)`
