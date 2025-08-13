# NinjaScript Reference (Minimal)

Read the official docs: https://developer.ninjatrader.com/docs/desktop

Common namespaces:
```csharp
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
```
Lifecycle:
- State.SetDefaults: set defaults, Name, Calculate
- State.Configure: AddDataSeries(), indicator instantiation
- State.DataLoaded: access indicators safely

Orders: Managed approach — `EnterLong/EnterShort`, `SetStopLoss(entrySignal, CalculationMode.Price, price, false)`, `ExitLongLimit/ExitShortLimit(qty, price, "TPx", entrySignal)`.

Troubleshooting:
- If `ATR`/`EMA` not found → add `using NinjaTrader.NinjaScript.Indicators;`
- If `Display` not found → add `using System.ComponentModel.DataAnnotations;`
