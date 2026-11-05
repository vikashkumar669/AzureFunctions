# Manuscript sources

Markdown sources for *Azure Functions for Dynamics 365 & Power Platform* (© 2026 Vikash Kumar, self-published).

| Path | Contents |
| --- | --- |
| `chapters/` | One Markdown file per chapter (`ch01-…` to `ch25-…`) and appendix (`appA-…` to `appI-…`) |
| `front/` | Preface |
| `figures/` | Diagrams (Mermaid / draw.io sources and rendered SVG) and screenshots |
| `capture-plan.csv` | Every screenshot to capture, with path, annotations and caption |
| `out/` | Build output (DOCX, PDF) – git-ignored |

| `templates/chapter-template.md` | The 21-section template every chapter starts from |
| `STYLE.md` | Writing, code, figure and accessibility rules |
| `build/` | Print CSS, Pandoc filter, Word reference document, fonts, build script |

## Building

```bash
# Needs pandoc 3.x, WeasyPrint, cairosvg, and Mermaid CLI (mmdc) for .mmd figures
book/build/build.sh book/chapters/*.md          # each chapter and appendix
python3 book/figures/make_figures.py             # diagrams (needs ICON_DIR)
python3 book/build/assemble.py                   # whole book: PDF + DOCX
# -> book/out/Azure-Functions-for-Dynamics-365.pdf and .docx
```

Code listings are not typed into chapters by hand: they are pulled from the compiled projects in `../src` by `// <listing id>` region markers, so the printed code always matches code that CI has built.

All companies, products, people and data in the book are fictional.
