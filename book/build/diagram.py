"""diagram.py - consistent, print-safe architecture diagrams for the book.

Every figure is a short Python script using this module. The canvas is
640 units wide (= the 5.98 in text column, about 0.67 pt per unit), so the
minimum text size of 11 units prints at about 7.4 pt (book/STYLE.md 5.1).

Text is measured with the real Inter font; any label that does not fit its
box raises an error instead of silently overflowing.

Microsoft product icons are embedded from a local icon library (env var
ICON_DIR) under Microsoft's terms for architecture diagrams and
documentation: icons are never altered, and each is labelled with the
product name next to it.
"""

from __future__ import annotations

import base64
import os
from dataclasses import dataclass, field
from xml.sax.saxutils import escape

from PIL import ImageFont

FONT_DIR = os.environ.get("INTER_DIR", "/usr/share/fonts/opentype/inter")
ICON_DIR = os.environ.get("ICON_DIR", "")

NAVY = "#0B1F3A"
AZURE = "#0F6CBD"
SKY = "#E8F2FB"
PANEL = "#F5F9FD"
SLATE = "#4A5568"
SOFT = "#C9D6E8"
AMBER = "#9A5100"
WHITE = "#FFFFFF"
TEAL = "#0E7C66"

_FONTS = {
    400: "Inter-Regular.otf",
    500: "Inter-Medium.otf",
    600: "Inter-SemiBold.otf",
    700: "Inter-Bold.otf",
}
_font_cache: dict[tuple[int, float], ImageFont.FreeTypeFont] = {}


def text_width(text: str, size: float, weight: int = 400) -> float:
    key = (weight, size)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(
            os.path.join(FONT_DIR, _FONTS[weight]), size=int(round(size * 4)))
    # measured at 4x for precision, scaled back
    return _font_cache[key].getlength(text) / 4.0


class FitError(ValueError):
    pass


@dataclass
class Diagram:
    width: int
    height: int
    label: str
    parts: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    icons_used: set[str] = field(default_factory=set)

    # ---------------------------------------------------------------- text
    def text(self, x, y, s, size=11, weight=400, color=SLATE,
             anchor="start", max_width=None):
        if size < 11:
            self.errors.append(f"text below 11 units: {s!r}")
        w = text_width(s, size, weight)
        if max_width is not None and w > max_width:
            self.errors.append(
                f"text {s!r} is {w:.0f} wide, room for {max_width:.0f}")
        left = x if anchor == "start" else x - w / 2 if anchor == "middle" else x - w
        if left < 0 or left + w > self.width:
            self.errors.append(f"text {s!r} leaves the canvas")
        self.parts.append(
            f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" '
            f'font-weight="{weight}" fill="{color}" '
            f'text-anchor="{anchor}">{escape(s)}</text>')
        return w

    def plate(self, x, y, s, size=11, weight=400, color=NAVY,
              anchor="middle"):
        """Text on a white plate, for labels that cross lines or borders."""
        w = text_width(s, size, weight)
        left = x if anchor == "start" else x - w / 2 if anchor == "middle" else x - w
        self.parts.append(
            f'<rect x="{left - 3:.1f}" y="{y - size:.1f}" width="{w + 6:.1f}" '
            f'height="{size + 4:.1f}" fill="{WHITE}"/>')
        self.text(x, y, s, size, weight, color, anchor)

    # --------------------------------------------------------------- icons
    def icon(self, name, x, y, size=28):
        if not ICON_DIR:
            return False
        path = os.path.join(ICON_DIR, f"{name}.svg")
        if not os.path.exists(path):
            self.errors.append(f"icon not found: {name}")
            return False
        data = base64.b64encode(open(path, "rb").read()).decode()
        self.parts.append(
            f'<image x="{x}" y="{y}" width="{size}" height="{size}" '
            f'href="data:image/svg+xml;base64,{data}"/>')
        self.icons_used.add(name)
        return True

    # ---------------------------------------------------------- containers
    def container(self, x, y, w, h, label, kind="azure", label_at="top",
                  sublabel=None):
        if kind == "boundary":
            self.parts.append(
                f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" '
                f'fill="{WHITE}" stroke="{NAVY}" stroke-width="1.2" '
                f'stroke-dasharray="6 4"/>')
        else:
            self.parts.append(
                f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" '
                f'fill="{PANEL}" stroke="{AZURE}" stroke-width="1.2"/>')
        ly = y + 22 if label_at == "top" else y + h - 12
        self.text(x + 14, ly, label, 12.5, 600, AZURE, max_width=w - 28)
        if sublabel:
            self.text(x + 14, ly + 16, sublabel, 11, 400, SLATE,
                      max_width=w - 28)

    # --------------------------------------------------------------- nodes
    def node(self, x, y, w, h, title, lines=(), kind="ms", icon=None,
             title_size=12.5, align=None):
        fills = {
            "ours": (NAVY, NAVY, WHITE, SOFT, ""),
            "ms": (WHITE, NAVY, NAVY, SLATE, ""),
            "ext": (SKY, AZURE, NAVY, SLATE, ""),
            "note": (WHITE, NAVY, NAVY, SLATE, ' stroke-dasharray="5 4"'),
            "accent": (AZURE, AZURE, WHITE, SKY, ""),
            "warn": ("#FFF4E0", AMBER, NAVY, SLATE, ""),
        }
        fill, stroke, tcol, lcol, dash = fills[kind]
        self.parts.append(
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="6" '
            f'fill="{fill}" stroke="{stroke}" stroke-width="1.2"{dash}/>')
        lines = list(lines)
        line_h = 15
        block = title_size + (len(lines) * line_h if lines else 0)
        if icon:
            isz = 28
            self.icon(icon, x + 10, y + (h - isz) / 2, isz)
            tx, anchor, avail = x + 48, "start", w - 58
        else:
            tx = x + w / 2 if align != "start" else x + 12
            anchor = "middle" if align != "start" else "start"
            avail = w - 20
        top = y + (h - block) / 2 + title_size * 0.8
        if block + 10 > h:
            self.errors.append(f"node {title!r}: content taller than box")
        self.text(tx, top, title, title_size, 600, tcol, anchor,
                  max_width=avail)
        for i, line in enumerate(lines):
            self.text(tx, top + (i + 1) * line_h, line, 11, 400, lcol,
                      anchor, max_width=avail)

    def cylinder(self, x, y, w, h, title, lines=()):
        rx, ry = w / 2, 12
        cx = x + rx
        self.parts.append(
            f'<path d="M{x} {y + ry}V{y + h - ry}A{rx} {ry} 0 0 0 '
            f'{x + w} {y + h - ry}V{y + ry}" fill="{WHITE}" '
            f'stroke="{NAVY}" stroke-width="1.2"/>')
        self.parts.append(
            f'<ellipse cx="{cx}" cy="{y + ry}" rx="{rx}" ry="{ry}" '
            f'fill="{WHITE}" stroke="{NAVY}" stroke-width="1.2"/>')
        mid = y + h / 2 + 4
        self.text(cx, mid, title, 13.5, 700, NAVY, "middle",
                  max_width=w - 16)
        for i, line in enumerate(lines):
            self.text(cx, mid + 16 * (i + 1), line, 11, 400, SLATE,
                      "middle", max_width=w - 16)

    # -------------------------------------------------------------- arrows
    def arrow(self, points, kind="normal", label=None, at=None,
              anchor="middle", both=False):
        color = AMBER if kind == "fail" else SLATE
        dash = ' stroke-dasharray="5 4"' if kind in ("fail", "async") else ""
        width = 1.8 if kind == "fail" else 1.5
        mid = "b" if kind == "fail" else "a"
        d = "M" + " L".join(f"{px} {py}" for px, py in points)
        start = f' marker-start="url(#{mid})"' if both else ""
        self.parts.append(
            f'<path d="{d}" fill="none" stroke="{color}" '
            f'stroke-width="{width}"{dash}{start} marker-end="url(#{mid})"/>')
        if label:
            lx, ly = at if at else (
                (points[0][0] + points[-1][0]) / 2,
                (points[0][1] + points[-1][1]) / 2 - 6)
            self.plate(lx, ly, label, 11,
                       600 if kind == "fail" else 400,
                       AMBER if kind == "fail" else NAVY, anchor)

    # -------------------------------------------------------------- legend
    def legend(self, y, items):
        x = 10
        for kind, label in items:
            if kind == "ours":
                self.parts.append(
                    f'<rect x="{x}" y="{y - 10}" width="20" height="12" '
                    f'rx="2" fill="{NAVY}"/>')
            elif kind == "boundary":
                self.parts.append(
                    f'<rect x="{x}" y="{y - 10}" width="20" height="12" '
                    f'rx="2" fill="{WHITE}" stroke="{NAVY}" '
                    f'stroke-dasharray="4 3"/>')
            elif kind == "fail":
                self.parts.append(
                    f'<path d="M{x} {y - 4}H{x + 22}" stroke="{AMBER}" '
                    f'stroke-width="1.8" stroke-dasharray="5 4"/>')
            elif kind == "async":
                self.parts.append(
                    f'<path d="M{x} {y - 4}H{x + 22}" stroke="{SLATE}" '
                    f'stroke-width="1.5" stroke-dasharray="5 4"/>')
            elif kind == "ext":
                self.parts.append(
                    f'<rect x="{x}" y="{y - 10}" width="20" height="12" '
                    f'rx="2" fill="{SKY}" stroke="{AZURE}"/>')
            w = self.text(x + 28, y, label, 11, 400, SLATE)
            x += 28 + w + 24

    # ---------------------------------------------------------------- save
    def svg(self) -> str:
        head = (
            f'<svg xmlns="http://www.w3.org/2000/svg" '
            f'viewBox="0 0 {self.width} {self.height}" width="{self.width}" '
            f'height="{self.height}" font-family="Inter, Arial, sans-serif" '
            f'role="img" aria-label="{escape(self.label)}">'
            f'<title>{escape(self.label)}</title><defs>'
            f'<marker id="a" viewBox="0 0 10 10" refX="9" refY="5" '
            f'markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
            f'<path d="M0 0L10 5L0 10z" fill="{SLATE}"/></marker>'
            f'<marker id="b" viewBox="0 0 10 10" refX="9" refY="5" '
            f'markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
            f'<path d="M0 0L10 5L0 10z" fill="{AMBER}"/></marker></defs>')
        return head + "".join(self.parts) + "</svg>\n"

    def save(self, path_without_ext: str):
        if self.errors:
            raise FitError(f"{path_without_ext}:\n  " +
                           "\n  ".join(self.errors))
        svg = self.svg()
        with open(path_without_ext + ".svg", "w") as f:
            f.write(svg)
        import cairosvg
        cairosvg.svg2png(bytestring=svg.encode(),
                         write_to=path_without_ext + ".png", scale=3,
                         background_color="white")
        return path_without_ext
