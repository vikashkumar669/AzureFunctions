"""Reports listing lines longer than 72 characters (STYLE.md 4)."""
import pathlib, re, sys
root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".")
bad = 0
for f in list(root.glob("src/**/*.cs")) + list(root.glob("samples/**/*.cs")) + list(root.glob("tests/**/*.cs")) + list(root.glob("infra/*.bicep")):
    inside = None
    for n, line in enumerate(f.read_text().splitlines(), 1):
        m = re.search(r"<listing:([\w-]+)>", line)
        if m and "</listing" not in line:
            inside = m.group(1); continue
        if "</listing:" in line:
            inside = None; continue
        if inside and len(line.rstrip()) > 72:
            bad += 1
            print(f"{f}:{n} [{inside}] {len(line.rstrip())}")
print(f"{bad} long lines")
