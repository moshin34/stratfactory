# Codex Prompt — Fix Compile Errors (Use repo template + NT docs)

You are an expert NinjaTrader 8 NinjaScript developer.
**Reference:** https://developer.ninjatrader.com/docs/desktop and this repo's docs/NT8_COMPILE_TARGET.md

Task:
A single-file strategy failed to compile. Fix the code while preserving IMMUTABLE Diagnostics/RTM regions
to match https://raw.githubusercontent.com/moshin34/stratfactory/main/templates/StrategyTemplate.cs.txt exactly.
Maintain namespace/class name/attributes and NT8 constraints.

Deliverable:
Return ONLY the full corrected `.cs` within ```csharp``` fencing, no explanations.

Paste below:
1) CURRENT FILE
2) COMPILER ERRORS (from knowledge/errors.log)
(Optionally paste bullets from knowledge/known_fixes.md at the top.)
