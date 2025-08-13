# NinjaTrader 8 Strategy Policy (Hard Compile Rules)
**Date:** 2025-08-13

These are **hard constraints** for any code merged into this repo. They reflect NinjaTrader 8’s dynamic compiler limits and our risk/rules plumbing.

## Compile Target & Language
- **.NET Framework:** 4.8
- **C# Language:** 7.3 (no features newer than 7.3)
- **One public class per .cs file** (no multi-class)
- **No partial classes** spanning files
- **Base type:** `Strategy` (or `Indicator` for indicators)
- **Allowed namespaces:** only those loaded by NinjaTrader 8 (no NuGet, no external DLLs)

## Banned features
- `async`, `await`, `Task`, `Thread`, `System.Threading`
- `dynamic`
- `record`, `init`
- Reflection-heavy code (`System.Reflection`) unless explicitly whitelisted
- Multiple public classes per file
- Public fields (use auto-properties only `{ get; set; }`)
- Overloads of NinjaScript lifecycle methods (e.g., custom `OnBarUpdate(int i)`)

## Required structure
- Keep two IMMUTABLE regions **byte-for-byte** identical to the official template:
  - `//== BEGIN IMMUTABLE: DIAGNOSTICS ==` → `//== END IMMUTABLE: DIAGNOSTICS ==`
  - `//== BEGIN IMMUTABLE: RTM ==` → `//== END IMMUTABLE: RTM ==`
- Only modify code between:
  - `//== BEGIN ENTRY LOGIC (EDITABLE) ==` and `//== END ENTRY LOGIC (EDITABLE) ==`

## Risk & Persistence (immutable)
- **PER-ACCOUNT ONLY** High-Water Mark (HWM) and BREACH markers.
- Trailing drawdown & circuit breaker use the persistent HWM.
- `StartNextSessionLockoutOnBreach` toggle controls one-time next-session lockout.
- `SessionCutoffTime` and `AutoFlatAtClose` must be honored.
- Immediate protective **flatten** on stop/target rejection or disconnect.

Violations are automatically blocked by CI.
