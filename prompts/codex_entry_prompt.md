# Codex Prompt — Entry-Only (Fetch template from repo)

You are an expert NinjaTrader 8 NinjaScript developer.
**Use and follow the official docs:** https://developer.ninjatrader.com/docs/desktop

Goal:
Return a single C# strategy file that compiles in NinjaTrader 8 (.NET 4.8, C# 7.3).
The template lives in this repo and contains IMMUTABLE Diagnostics + RTM. 
Fetch it from:
https://raw.githubusercontent.com/moshin34/stratfactory/main/templates/StrategyTemplate.cs.txt

You ONLY implement ENTRY logic between these markers:
//== BEGIN ENTRY LOGIC (EDITABLE) ==
//== END ENTRY LOGIC (EDITABLE) ==

Constraints (also see docs/NT8_COMPILE_TARGET.md):
- Namespace: Standalone.Strategies; Class name == file name.
- Do NOT modify IMMUTABLE regions.
- Managed orders only; exits/stops handled by RTM.
- Add entry parameters with [NinjaScriptProperty], [Display] (with units), [Range].
- Respect lockout (no entries during CircuitBreaker/PropDD lockout).

Deliverable:
Return ONLY the full `.cs` in ```csharp``` fencing. No explanations.

Now generate a strategy named {StrategyName} using the SPEC below.
