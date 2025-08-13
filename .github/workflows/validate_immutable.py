# Validates that IMMUTABLE regions in all .cs files match the canonical text in the template.
import os, re, sys, pathlib

root = pathlib.Path(".")
template_path = root / "templates" / "StrategyTemplate.cs.txt"
if not template_path.exists():
    print("Template not found for validation:", template_path)
    sys.exit(1)

text = template_path.read_text(encoding="utf-8", errors="ignore")
def extract(kind):
    m = re.search(r"//== BEGIN IMMUTABLE: %s ==(.*?)//== END IMMUTABLE: %s ==" % (kind, kind), text, re.S)
    if not m:
        print("Could not find IMMUTABLE region in template:", kind)
        sys.exit(1)
    # Strip #region/#endregion for stable comparison
    block = m.group(1)
    block = re.sub(r"^\s*#region.*?$|^\s*#endregion\s*$", "", block, flags=re.M).strip()
    return block

canon_diag = extract("DIAGNOSTICS")
canon_rtm  = extract("RTM")

pattern = re.compile(r"//== BEGIN IMMUTABLE: (DIAGNOSTICS|RTM) ==(.*?)//== END IMMUTABLE: \1 ==", re.S)
bad = []
for path in root.rglob("*.cs"):
    if path.name.endswith("Template.cs.txt"):
        continue
    try:
        t = path.read_text(encoding="utf-8", errors="ignore")
    except Exception as e:
        continue
    for m in pattern.finditer(t):
        kind = m.group(1)
        blk = re.sub(r"^\s*#region.*?$|^\s*#endregion\s*$", "", m.group(2), flags=re.M).strip()
        canon = canon_diag if kind == "DIAGNOSTICS" else canon_rtm
        if blk != canon:
            bad.append((str(path), kind))

if bad:
    print("Immutable region mismatch in:")
    for p,k in bad:
        print(f"- {p} [{k}]")
    sys.exit(1)
print("Immutable regions OK.")
