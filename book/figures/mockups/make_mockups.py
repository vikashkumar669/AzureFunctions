"""Renders the illustrative UI mock-ups listed in book/capture-plan.csv.

    python3 book/figures/mockups/make_mockups.py [03/core-tools ...]

Each mock-up is built from generic components (kit.py) and fictional data
(specs_a.py, specs_b.py), rendered with Playwright/Chromium, and written to
book/figures/captures/<NN>-<id>.png. The book filter uses that PNG in place
of the capture placeholder and captions it "Illustrative mock-up – not a
screenshot". Replace a PNG with a real capture (same name) when available.
"""

import csv
import os
import sys

import kit
from specs_a import S as A
from specs_b import S as B
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
BOOK = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(BOOK, "figures", "captures")
kit.FONT_DIR = os.path.join(BOOK, "build", "fonts")

SPECS = {**A, **B}


def main(only):
    plan = [r["figure"] for r in csv.DictReader(open(os.path.join(BOOK, "capture-plan.csv")))]
    missing = [f for f in plan if f not in SPECS]
    if missing:
        sys.exit("No mock-up spec for: " + ", ".join(missing))
    os.makedirs(OUT, exist_ok=True)
    todo = only or plan
    with sync_playwright() as p:
        browser = p.chromium.launch()
        pg = browser.new_page(viewport={"width": kit.WIDTH + 40, "height": 600},
                              device_scale_factor=2.5)
        for fig in todo:
            pg.set_content(kit.page(SPECS[fig]()))
            pg.wait_for_load_state("networkidle")
            name = fig.replace("/", "-") + ".png"
            over = pg.evaluate("""() => { const w = document.querySelector('.win').getBoundingClientRect();
                return [...document.querySelectorAll('.win *')].filter(e => {
                  const r = e.getBoundingClientRect();
                  return r.width && (r.right > w.right + 1 || e.scrollWidth > e.clientWidth + 2)
                    && getComputedStyle(e).overflow !== 'hidden';
                }).map(e => e.className + ':' + e.textContent.slice(0, 40)); }""")
            if over:
                print("  OVERFLOW", fig, over[:4])
            pg.locator(".win").screenshot(path=os.path.join(OUT, name))
            print("mock-up:", name)
        browser.close()


if __name__ == "__main__":
    main(sys.argv[1:])
