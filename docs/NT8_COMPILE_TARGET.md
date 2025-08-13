# NT8 COMPILE TARGET

- Target Framework: .NET Framework 4.8
- C# Language: 7.3 (avoid records/init/newer pattern matching)
- Exactly one public class per file; derives from Strategy
- Use namespaces:
  - using NinjaTrader.NinjaScript;
  - using NinjaTrader.NinjaScript.Strategies;
  - using NinjaTrader.NinjaScript.Indicators;
  - using NinjaTrader.Cbi;
  - using NinjaTrader.Data;
  - using System.ComponentModel.DataAnnotations;
- No NuGet/external DLLs (except what NT8 already loads)
- No async/await/dynamic/records/init; avoid threading
- Use State model: SetDefaults, Configure, DataLoaded, etc.
