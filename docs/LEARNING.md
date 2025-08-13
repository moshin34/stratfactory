# LEARNING: Prevent Repeat Compile Errors

1) When NT8 compile fails, copy the full compiler output into `knowledge/errors.log` (append at bottom).
2) When asking Codex to fix, include:
   - The full current `.cs` file
   - The exact error text from `knowledge/errors.log`
   - Optionally paste relevant bullets from `knowledge/known_fixes.md`
3) Ask Codex: “Return the complete corrected `.cs` file and do not change IMMUTABLE regions.”

Notes:
- NT8 compiles all scripts in one batch; a typo elsewhere can break your new file.
- Always ensure namespace == `Standalone.Strategies` and class name == file name.
