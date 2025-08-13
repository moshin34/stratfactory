# Model-Facing Contract (Codex must honor these)
**Use the repository template at** the raw URL for `/templates/StrategyTemplate.cs.txt` and **only** add code between:
`//== BEGIN ENTRY LOGIC (EDITABLE) ==` and `//== END ENTRY LOGIC (EDITABLE) ==`.

Non-negotiable rules:
1) Leave the IMMUTABLE DIAGNOSTICS & RTM blocks **byte-for-byte identical**.
2) **PER-ACCOUNT ONLY** for HWM and BREACH persistence. Do not key by instrument or strategy.
3) Target **NinjaTrader 8**, .NET 4.8, **C# 7.3**. No async/await/dynamic/record/init/Task/Thread.
4) One public class per file; derive from `Strategy`. Avoid LINQ in hot paths.
5) Don’t re-declare properties that the template already defines.

Deliver a single `.cs` file named `<StrategyName>.cs` with class in the configured namespace. It must compile the first time in NT8.
