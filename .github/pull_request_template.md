## PR Checklist (Codex & Humans)

- [ ] I used the repo template and **only** edited code between ENTRY markers.
- [ ] The IMMUTABLE `DIAGNOSTICS` and `RTM` blocks are **unchanged** (byte-for-byte).
- [ ] No banned features: `async/await`, `Task/Thread`, `dynamic`, `record`, `init`, reflection.
- [ ] One public class per file, derives from `Strategy`.
- [ ] **PER-ACCOUNT ONLY** HWM/ BREACH persistence is respected (no instrument/strategy in keys).
- [ ] Uses .NET 4.8, C# 7.3-compatible syntax.
- [ ] Risk toggles, cutoff/auto-flat, BE/TP/Trail are driven by UI properties (not hardcoded).
