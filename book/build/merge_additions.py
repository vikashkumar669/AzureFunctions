"""One-off helper: merges book/chapters/_add/chNN.md into the chapters.

Each addition file has a %%DEEP%% part (inserted before "Screens you will
use", or before "Complete working code") and a %%LAB%% part (inserted
before "Hands-on exercise"). A merged chapter is marked so it is never
merged twice. Paths written in backticks are checked against the repo.
"""

import glob
import os
import re
import sys

BOOK = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(BOOK)
MARK = "<!-- expanded: deep dive + guided lab -->"


def insert_before(text, headings, block):
    for h in headings:
        i = text.find("\n" + h + "\n")
        if i >= 0:
            return text[:i + 1] + block.strip() + "\n\n" + text[i + 1:]
    sys.exit(f"none of {headings} found")


def check_paths(name, block):
    bad = []
    for p in re.findall(r"`((?:src|samples|tests|infra|book)/[^`\s]+)`", block):
        p = p.rstrip("/")
        if "<" in p or "…" in p or "*" in p:
            continue
        if not os.path.exists(os.path.join(ROOT, p)):
            bad.append(p)
    if bad:
        sys.exit(f"{name}: paths not in repo: {bad}")


def main(names):
    for add in sorted(glob.glob(os.path.join(BOOK, "chapters", "_add", "ch*.md"))):
        nn = os.path.basename(add)[2:4]
        if names and nn not in names:
            continue
        chapter = glob.glob(os.path.join(BOOK, "chapters", f"ch{nn}-*.md"))[0]
        text = open(chapter, encoding="utf-8").read()
        if MARK in text:
            continue
        src = open(add, encoding="utf-8").read()
        deep, lab = src.split("%%LAB%%")
        deep = deep.replace("%%DEEP%%", "")
        check_paths(nn, src)
        text = insert_before(text, ["## Screens you will use", "## Complete working code",
                                    "## Common errors and troubleshooting",
                                    "## Security considerations"], deep)
        text = insert_before(text, ["## Hands-on exercise"], lab)
        text = text.rstrip("\n") + "\n\n" + MARK + "\n"
        open(chapter, "w", encoding="utf-8").write(text)
        print("merged", os.path.basename(chapter))


if __name__ == "__main__":
    main(sys.argv[1:])
