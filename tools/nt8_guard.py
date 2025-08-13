#!/usr/bin/env python3
import sys, re, pathlib, difflib

TEMPLATE = pathlib.Path("templates/StrategyTemplate.cs.txt").read_text(encoding="utf-8", errors="ignore")

def extract(block_name, text):
    start = f"//== BEGIN IMMUTABLE: {block_name} =="
    end   = f"//== END IMMUTABLE: {block_name} =="
    s = text.find(start)
    e = text.find(end)
    if s == -1 or e == -1:
        return None
    return text[s:e+len(end)]

def main():
    t_diag = extract("DIAGNOSTICS", TEMPLATE)
    t_rtm  = extract("RTM", TEMPLATE)
    if t_diag is None or t_rtm is None:
        print("template missing immutable blocks")
        sys.exit(2)

    failures = []
    for cs in pathlib.Path(".").rglob("*.cs"):
        if cs.name.endswith(".g.cs"):
            continue
        text = cs.read_text(encoding="utf-8", errors="ignore")
        d = extract("DIAGNOSTICS", text)
        r = extract("RTM", text)
        missing = []
        if d is None: missing.append("DIAGNOSTICS")
        if r is None: missing.append("RTM")
        if missing:
            # Only flag if this looks like a strategy (has ENTRY markers)
            if "//== BEGIN ENTRY LOGIC (EDITABLE) ==" in text:
                failures.append((str(cs), f"missing immutable blocks: {', '.join(missing)}"))
            continue
        if d != t_diag:
            diff = ''.join(difflib.unified_diff(t_diag.splitlines(True), d.splitlines(True), fromfile="template:DIAGNOSTICS", tofile=str(cs)+":DIAGNOSTICS"))
            failures.append((str(cs), "DIAGNOSTICS mismatch\n"+diff))
        if r != t_rtm:
            diff = ''.join(difflib.unified_diff(t_rtm.splitlines(True), r.splitlines(True), fromfile="template:RTM", tofile=str(cs)+":RTM"))
            failures.append((str(cs), "RTM mismatch\n"+diff))

    if failures:
        print("IMMUTABLE GUARD FAILURES:")
        for f, msg in failures:
            print(f"::group::{f}")
            print(msg)
            print("::endgroup::")
        sys.exit(1)
    print("Immutable guard passed.")
    sys.exit(0)

if __name__ == "__main__":
    main()
