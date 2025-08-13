# Known Fixes
- Add `using NinjaTrader.NinjaScript.Indicators;` when indicator types are unknown.
- Add `using System.ComponentModel.DataAnnotations;` for [Display]/[Range] attributes.
- Namespace must be Standalone.Strategies and class name must equal file name.
- Use SetStopLoss(entrySignal, CalculationMode.Price, price, false).
- Partial exits must reference the entry signal: ExitLongLimit(qty, price, "TP1", "ENTRY").
