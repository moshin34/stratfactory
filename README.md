# Standalone Strategy Factory (Codex-ready)

**Goal:** Paste a SPEC into Codex and get back a fully self-contained, compile-ready NinjaTrader 8 strategy `.cs` file
with standardized risk/SL/TP/circuit breaker/diagnostics built-in.

## What’s Inside
- `templates/StrategyTemplate.cs.txt` — master template with **IMMUTABLE** Diagnostics + Risk/Trade Management (RTM)
- `prompts/codex_entry_prompt.md` — the only prompt you need to generate a strategy
- `prompts/codex_fix_prompt.md` — use if compile errors occur
- `docs/NT8_COMPILE_TARGET.md` — NT8 compile constraints (C# 7.3, .NET 4.8, one class per file, etc.)
- `docs/NinjaScript_Reference.md` — quick reference and links to official docs
- `docs/LEARNING.md` — how to “learn from compile errors” using Codex fix loop
- `knowledge/known_fixes.md` — paste these bullets into fix prompts to speed up correction
- `.github/workflows/guard-immutable.yml` + validator — rejects changes to IMMUTABLE blocks in PRs

## One-time Setup
1) Upload this bundle to a new GitHub repo (private or public).
2) After you push, **get the raw URL** of `templates/StrategyTemplate.cs.txt` on GitHub.
   - Example: `https://raw.githubusercontent.com/<you>/<repo>/main/templates/StrategyTemplate.cs.txt`
3) Open `prompts/codex_entry_prompt.md` and replace `<RAW_URL_TO>` with your repo’s raw URL prefix.

## Daily Workflow (no boilerplate pasting)
1) Open `prompts/codex_entry_prompt.md` and copy the whole prompt.
2) In Codex UI:
   - Paste the prompt
   - Fill `StrategyName` and paste your SPEC (entry-only)
   - Submit
3) Codex fetches the template from your repo, inserts your entry logic (between ENTRY markers), and returns a full `.cs`.
4) Save as `Documents/NinjaTrader 8/bin/Custom/Strategies/<StrategyName>.cs`
5) In NT8, compile and enable.

## If It Doesn’t Compile (learning loop)
1) Copy the full compiler errors into `knowledge/errors.log` (append at bottom).
2) Open `prompts/codex_fix_prompt.md`, paste:
   - the full current `.cs`
   - the error text you just saved
   - optionally, a few bullets from `knowledge/known_fixes.md`
3) Submit to Codex → replace your `.cs` with the corrected file → compile again.

## Notes
- All risk/exit settings are exposed as **UI parameters** — nothing is hardcoded. This makes Strategy Analyzer optimization easy.
- The template includes a **circuit breaker**, **daily loss**, **PropTrailingDD**, **breakeven**, **TP1/TP2/Trail**, and **verbose diagnostics/logging** (optional JSONL export).
- CI guardrails will block any attempt to modify IMMUTABLE regions in PRs, ensuring consistent behavior across all strategies.
