#!/usr/bin/env bash
# Builds PDF (print layout) and DOCX (editable manuscript) for chapters.
#
#   book/build/build.sh book/chapters/ch06-*.md      # one or more files
#
# Needs: pandoc 3.x, WeasyPrint, Mermaid CLI (mmdc) for .mmd figures.
#   MMDC=/path/to/mmdc   PUPPETEER_CFG=/path/to/puppeteer.json  (optional)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOK="$(dirname "$HERE")"
CODE_ROOT="$(dirname "$BOOK")"          # the repository root
OUT="$BOOK/out"
MMDC="${MMDC:-mmdc}"

mkdir -p "$OUT/fonts"
cp "$HERE/book.css" "$OUT/"
cp "$HERE"/fonts/*.woff2 "$OUT/fonts/"

# 1. Diagrams: Mermaid sources -> 4x PNG, used by both PDF and DOCX.
#    (WeasyPrint does not render the HTML labels inside Mermaid SVGs.)
if find "$BOOK/figures" -name '*.mmd' | grep -q .; then
  while IFS= read -r src; do
    base="${src%.mmd}"
    if [[ ! -f "$base.png" || "$src" -nt "$base.png" \
          || "$HERE/mermaid-config.json" -nt "$base.png" ]]; then
      echo "mermaid: ${src#$BOOK/}"
      args=(-c "$HERE/mermaid-config.json" -i "$src" -b white)
      [[ -n "${PUPPETEER_CFG:-}" ]] && args+=(-p "$PUPPETEER_CFG")
      "$MMDC" "${args[@]}" -o "$base.png" -s 4 >/dev/null
    fi
  done < <(find "$BOOK/figures" -name '*.mmd')
fi
# Hand-drawn SVG diagrams: PDF uses the SVG, DOCX needs a PNG copy.
while IFS= read -r svg; do
  png="${svg%.svg}.png"
  if [[ ! -f "$png" || "$svg" -nt "$png" ]]; then
    echo "svg->png: ${svg#$BOOK/}"
    python3 -c "import sys, cairosvg; cairosvg.svg2png(url=sys.argv[1], \
write_to=sys.argv[2], scale=3, background_color='white')" "$svg" "$png"
  fi
done < <(find "$BOOK/figures" -name '*.svg')

rm -rf "$OUT/figures"
cp -r "$BOOK/figures" "$OUT/figures"

# 2. Chapters.
for md in "$@"; do
  name="$(basename "${md%.md}")"
  echo "chapter: $name"

  pandoc "$md" \
    --from markdown --to html5 --standalone \
    --template "$HERE/template.html" \
    --lua-filter "$HERE/filters/book.lua" \
    --metadata code-root="$CODE_ROOT" \
    --output "$OUT/$name.html"

  weasyprint --base-url "$OUT/" "$OUT/$name.html" "$OUT/$name.pdf"

  pandoc "$md" \
    --from markdown --to docx \
    --reference-doc "$HERE/reference.docx" \
    --lua-filter "$HERE/filters/book.lua" \
    --metadata code-root="$CODE_ROOT" \
    --resource-path "$BOOK" \
    --output "$OUT/$name.docx"
done

echo "Output in ${OUT#$CODE_ROOT/}/"
