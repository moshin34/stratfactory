# Codex Prompt — Generate Standalone NinjaScript Strategy (Template in Repo)

You are an expert NinjaTrader 8 NinjaScript developer.
Use the official docs as needed: https://developer.ninjatrader.com/docs/desktop
Follow repo compile constraints: docs/NT8_COMPILE_TARGET.md

Instructions:
1) Fetch the latest template from this repo at: <RAW_URL_TO>/templates/StrategyTemplate.cs.txt
2) Replace <StrategyName> with the given strategy name.
3) Edit ONLY between these markers:
//== BEGIN ENTRY LOGIC (EDITABLE) ==
//== END ENTRY LOGIC (EDITABLE) ==
4) Do not modify or remove the IMMUTABLE blocks (Diagnostics + RTM).
5) Respect NT8 constraints (C# 7.3, .NET 4.8, one class per file).
6) Use only built-in NinjaTrader indicators/APIs.
7) Expose any entry-specific parameters with [NinjaScriptProperty], [Display] (include units), and [Range].
8) Return ONLY the full `.cs` file, fenced in ```csharp.

Inputs you will receive from me now:
- StrategyName: <fill me>
- SPEC (entry-only): paste below

## SPEC
