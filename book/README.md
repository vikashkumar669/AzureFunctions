# Manuscript sources

Markdown sources for *Azure Functions for Dynamics 365 & Power Platform* (© 2026 Vikash Kumar, self-published).

| Path | Contents |
| --- | --- |
| `chapters/` | One Markdown file per chapter (`ch01-…` to `ch25-…`) and appendix (`appA-…` to `appN-…`) |
| `chapters/_add/` | Sources of the deep dives and guided labs, already merged into the chapters by `build/merge_additions.py` (kept for history) |
| `front/` | Preface |
| `figures/` | Diagrams (sources and rendered SVG/PNG) |
| `figures/mockups/` | Generator for the illustrative screen mock-ups (`make_mockups.py`, `kit.py`, `specs_*.py`) |
| `figures/captures/` | Rendered mock-ups, one PNG per screen; replace a file with a real capture of the same name to use it instead |
| `capture-plan.csv` | Every screen: mock-up file, path, annotations, caption, and what a real capture must show and mask |
| `out/` | Build output (DOCX, PDF) – git-ignored |
| `templates/chapter-template.md` | The 21-section template every chapter starts from |
| `STYLE.md` | Writing, code, figure and accessibility rules |
| `build/` | Print CSS, Pandoc filter, Word reference document, fonts, build script |

## Building

```bash
# Needs pandoc 3.x, WeasyPrint, cairosvg, and Mermaid CLI (mmdc) for .mmd figures
book/build/build.sh book/chapters/*.md          # each chapter and appendix
python3 book/figures/make_figures.py             # diagrams (needs ICON_DIR)
python3 book/figures/mockups/make_mockups.py     # screen mock-ups (Playwright + Chromium)
python3 book/build/assemble.py                   # whole book: PDF + DOCX
# -> book/out/Azure-Functions-for-Dynamics-365.pdf and .docx
```

Code listings are not typed into chapters by hand: they are pulled from the compiled projects in `../src` by `// <listing id>` region markers, so the printed code always matches code that CI has built.

Screens are shown as illustrative mock-ups, labelled as such in the image and the caption; they are not screenshots.

All companies, products, people and data in the book are fictional.
