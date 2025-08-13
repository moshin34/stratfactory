# Codex Entry Prompt — Standalone NinjaScript Strategy (PER-ACCOUNT HWM REQUIRED)

**Critical, immutable requirements:**
- Use the repo template at this raw URL: <PASTE YOUR RAW templates/StrategyTemplate.cs.txt URL>
- Only edit inside `//== BEGIN ENTRY LOGIC (EDITABLE) ==` and `//== END ENTRY LOGIC (EDITABLE) ==`
- **PER-ACCOUNT ONLY** persistence for High-Water Mark (HWM) and BREACH markers. Do **NOT** key by instrument or strategy.
- Keep the DIAGNOSTICS and RTM blocks byte-for-byte identical to the template.
- Follow NinjaTrader 8 constraints: .NET 4.8, C# 7.3, single public class per file, Strategy in `Standalone.Strategies`.
- Use only supported namespaces; avoid async/await, dynamic, threads, records, init.

My SPEC (entry conditions only; the plumbing handles risk/exits/BE/TP/Trail):
- [Describe your entry logic, filters, timeframes, instruments, etc.]

Deliverable:
- A single **compilable** `.cs` file named `<StrategyName>.cs` with class `Standalone.Strategies.<StrategyName>`,
- That compiles in NinjaTrader 8 **first try**, using only the ENTRY region for custom code.
- Do not modify or recreate any properties that already exist in the template.
