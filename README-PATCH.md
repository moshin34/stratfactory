# Standalone Strategy Factory — Patch (2025-08-13)

This patch ensures your template contains the full IMMUTABLE Diagnostics + RTM code
and that prompts are pre-wired to fetch the template from your repo.

## Files added/updated
- templates/StrategyTemplate.cs.txt — full template (Diagnostics + RTM + ENTRY markers)
- .github/workflows/guard-immutable.yml + validate_immutable.py — block edits to IMMUTABLE regions
- prompts/codex_entry_prompt.md — fetches template from: https://raw.githubusercontent.com/moshin34/stratfactory/main/templates/StrategyTemplate.cs.txt
- prompts/codex_fix_prompt.md — repair prompt using repo template
- docs/NT8_COMPILE_TARGET.md, docs/NinjaScript_Reference.md, docs/LEARNING.md
- knowledge/known_fixes.md, knowledge/errors.log

## Use
1) Commit these files to your repo (overwrite existing if prompted).
2) In Codex, open prompts/codex_entry_prompt.md, replace {StrategyName} and paste your SPEC.
3) Save returned `.cs` to `Documents/NinjaTrader 8/bin/Custom/Strategies/{StrategyName}.cs` and compile.
4) If errors appear, append compiler output to knowledge/errors.log and use prompts/codex_fix_prompt.md.
