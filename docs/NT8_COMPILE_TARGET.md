# NinjaTrader 8 Compile Target (Quick Reference)

Authoritative docs: https://developer.ninjatrader.com/docs/desktop

Framework: .NET 4.8
Language: C# 7.3 (no records/init/modern pattern matching/async/dynamic)
Compiler: NT8 internal CSharpCodeProvider

One class per file. Strategies in `NinjaTrader.NinjaScript.Strategies`. Indicators in `NinjaTrader.NinjaScript.Indicators`.
Mandatory methods: OnStateChange(), OnBarUpdate(), OnOrderUpdate(Order), OnExecutionUpdate(Execution, Order).

Avoid: threading, dynamic, heavy LINQ in OnBarUpdate, reflection.
