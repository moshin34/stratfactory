# Codex Prompt — Fix Compile Errors (Keep IMMUTABLE Blocks Intact)

You are an expert NinjaTrader 8 NinjaScript developer.
Use: https://developer.ninjatrader.com/docs/desktop and docs/NT8_COMPILE_TARGET.md

Task:
- Fix compile errors in the provided single-file strategy.
- Preserve the IMMUTABLE Diagnostics + RTM blocks unless the error explicitly points inside them.
- Keep namespace/class name/attributes intact.

Deliverable:
- Return ONLY the full corrected `.cs` in ```csharp``` fencing. No explanations.

Context to include:
- CURRENT FILE: paste full `.cs`
- ERRORS: paste full compiler output (also added to knowledge/errors.log)
- KNOWN FIXES (optional): paste relevant bullets from knowledge/known_fixes.md
