#!/usr/bin/env python3
import sys, re, json, pathlib

def load_cfg(path):
    try:
        return json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    except Exception:
        return {}

CFG = load_cfg("config/lint.config.json")

BANNED = CFG.get("banned_tokens", [])
ALLOW_NS = set(CFG.get("allow_namespaces", []))
EXPECT_NS = CFG.get("expected_namespace")
MAX_CLASSES = int(CFG.get("max_public_classes_per_file", 1))
ENFORCE_SINGLE = bool(CFG.get("enforce_single_class", True))
CHECK_PUBLIC_FIELDS = bool(CFG.get("check_public_fields", True))

def grep(pattern, text, flags=0):
    return list(re.finditer(pattern, text, flags))

def check_file(path):
    text = pathlib.Path(path).read_text(encoding="utf-8", errors="ignore")
    errs = []

    # banned tokens
    for tok in BANNED:
        if tok in text:
            errs.append(f"banned token found: {tok.strip()}")

    # namespace (advisory if not present)
    ns_m = re.search(r'namespace\s+([A-Za-z0-9_.]+)', text)
    if ns_m:
        ns = ns_m.group(1)
        if ALLOW_NS and ns not in ALLOW_NS:
            errs.append(f"namespace '{ns}' not in allowed set {sorted(ALLOW_NS)}")
        if EXPECT_NS and ns != EXPECT_NS:
            errs.append(f"namespace '{ns}' != expected '{EXPECT_NS}' (update config if intentional)")
    else:
        errs.append("no namespace declared")

    # class counting
    classes = grep(r'\bclass\s+([A-Za-z_][A-Za-z0-9_]*)', text)
    pub_classes = grep(r'\bpublic\s+class\s+([A-Za-z_][A-Za-z0-9_]*)', text)
    if ENFORCE_SINGLE and len(classes) > 1:
        errs.append(f"multiple classes found ({len(classes)}); only one allowed")
    if len(pub_classes) > MAX_CLASSES:
        errs.append(f"multiple public classes found ({len(pub_classes)}); max {MAX_CLASSES}")

    # base type must include Strategy/Indicator
    if not re.search(r'class\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*(?:\w+\s*,\s*)*(Strategy|Indicator)\b', text):
        errs.append("base type must derive from Strategy or Indicator")

    # public fields (naive): public <type> <name>; not a property or const
    if CHECK_PUBLIC_FIELDS:
        for m in grep(r'\bpublic\s+(?!class|interface|enum|struct)\s+[A-Za-z0-9_<>,\[\]\.?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*;', text):
            seg = text[m.start()-50:m.end()+50]
            if " get;" in seg and " set;" in seg:
                continue
            if " const " in seg:
                continue
            errs.append("public field detected (use property with get; set;): " + m.group(0).strip())

    return errs

def main():
    files = [p for p in sys.argv[1:] if p.endswith('.cs')]
    if not files:
        files = [str(p) for p in pathlib.Path('.').rglob('*.cs')]
    all_errs = []
    for f in files:
        errs = check_file(f)
        if errs:
            all_errs.append((f, errs))
    if all_errs:
        print("NT8 LINT FAILURES:")
        for f, errs in all_errs:
            print(f"::group::{f}")
            for e in errs:
                print(f" - {e}")
            print("::endgroup::")
        sys.exit(1)
    print("NT8 lint passed.")
    sys.exit(0)

if __name__ == "__main__":
    main()
