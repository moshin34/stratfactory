# Strategy Template v1.1 — Persistent HWM + Cutoff/AutoFlat + Failsafes
Date: 2025-08-13

Drop `StrategyTemplate.cs.txt` into your repo at `templates/` and commit.

Changes:
- Persistent high-water mark (HWM) for trailing drawdown (never daily reset), with optional file persistence (default ON).
- `SessionCutoffTime` + `AutoFlatAtClose`: blocks new entries after cutoff and flattens open positions at/after cutoff.
- Fail-safes: Flatten & lockout on stop/target rejection; flatten & lockout on disconnect heuristic.
- ENTRY remains the only editable region.

No API usage. Compile target unchanged (.NET 4.8, C# 7.3).
