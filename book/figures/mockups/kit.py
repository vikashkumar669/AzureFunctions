"""Component kit for illustrative UI mock-ups.

Every mock-up is drawn from simple, generic components (window frame,
top bar, breadcrumb, form fields, tables, terminal, code editor, grid).
No product logos or icons are drawn, and every image carries an
"Illustrative mock-up" tag, so it can't be mistaken for a screenshot.

Inline markers: "{1}" anywhere in text becomes a numbered callout badge
matching the annotation list in capture-plan.csv.
"""

import html
import re

WIDTH = 820

THEMES = {
    # name: (top bar background, top bar text, accent)
    "azure":    ("#1B2433", "#FFFFFF", "#2B6CD4"),
    "admin":    ("#24292F", "#FFFFFF", "#3A6E9E"),
    "maker":    ("#2E2A44", "#FFFFFF", "#6A4FB4"),
    "d365":     ("#0F2A47", "#FFFFFF", "#2B6CD4"),
    "entra":    ("#1F2D3D", "#FFFFFF", "#2B7A9E"),
    "github":   ("#24292F", "#FFFFFF", "#2DA44E"),
    "dts":      ("#14202E", "#FFFFFF", "#0E8C7C"),
    "excel":    ("#1E5E3B", "#FFFFFF", "#1E7A47"),
    "vscode":   ("#2B2B2B", "#DDDDDD", "#3A8EE6"),
    "terminal": ("#1E1E1E", "#DDDDDD", "#3A8EE6"),
    "dialog":   ("#E9EDF2", "#1B2433", "#2B6CD4"),
}

CSS = """
* { box-sizing: border-box; }
body { margin: 0; padding: 14px; background: #FFFFFF;
  font: 400 14px/1.4 Inter, 'Segoe UI', Arial, sans-serif; color: #1B2433; }
.win { width: %(w)dpx; border: 1px solid #B9C3CF; border-radius: 8px;
  overflow: hidden; box-shadow: 0 2px 8px rgba(20,30,50,.12); background: #fff; }
.titlebar { display: flex; align-items: center; gap: 8px; height: 30px;
  padding: 0 12px; background: #EEF1F5; border-bottom: 1px solid #D5DCE4;
  font-size: 12px; color: #4A5668; }
.dots span { display: inline-block; width: 10px; height: 10px;
  border-radius: 50%%; background: #C9D1DB; margin-right: 5px; }
.titlebar .t { flex: 1; text-align: center; }
.tag { font: 700 10px/1 Inter, sans-serif; letter-spacing: .6px;
  color: #8A5A00; background: #FFF3D6; border: 1px solid #E5C477;
  padding: 3px 6px; border-radius: 3px; text-transform: uppercase; }
.top { display: flex; align-items: center; gap: 14px; height: 38px;
  padding: 0 14px; font-weight: 600; font-size: 13px; }
.top .search { flex: 1; max-width: 300px; height: 24px; border-radius: 4px;
  background: rgba(255,255,255,.14); font-weight: 400; font-size: 12px;
  padding: 4px 10px; color: rgba(255,255,255,.7); }
.top .who { margin-left: auto; font-weight: 400; font-size: 12px; opacity: .85; }
.crumb { padding: 9px 16px 0; font-size: 12px; color: #4A5668; }
.crumb b { color: #1B2433; font-weight: 500; }
.layout { display: flex; min-height: 120px; }
.nav { width: 178px; flex: none; border-right: 1px solid #E1E6EC;
  padding: 10px 0; font-size: 12.5px; background: #FAFBFC; }
.nav div { padding: 5px 16px; color: #3B4757; }
.nav .h { font-weight: 600; color: #6B7686; font-size: 11px;
  text-transform: uppercase; letter-spacing: .5px; padding-top: 10px; }
.nav .on { background: #E6EEF9; color: #1B2433; font-weight: 600;
  border-left: 3px solid var(--accent); padding-left: 13px; }
.main { flex: 1; padding: 12px 18px 16px; min-width: 0; }
h1 { font-size: 19px; font-weight: 600; margin: 4px 0 10px; }
h2 { font-size: 14px; font-weight: 600; margin: 14px 0 6px; }
.cmds { display: flex; gap: 14px; padding: 4px 0 10px; font-size: 12.5px;
  color: var(--accent); border-bottom: 1px solid #E1E6EC; margin-bottom: 10px; }
.tabs { display: flex; gap: 18px; border-bottom: 1px solid #E1E6EC;
  margin: 2px 0 12px; font-size: 13px; }
.tabs div { padding: 6px 0; color: #4A5668; }
.tabs .on { color: #1B2433; font-weight: 600; border-bottom: 2px solid var(--accent); }
.fields { display: grid; grid-template-columns: minmax(110px, 190px) 1fr; gap: 7px 14px;
  font-size: 13px; align-items: center; }
.fields .l { color: #4A5668; overflow-wrap: anywhere; }
.fields .v { min-height: 26px; padding: 4px 8px; border: 1px solid #C9D1DB;
  border-radius: 3px; background: #fff; }
.fields .v.ro { border-color: transparent; background: transparent; padding-left: 0; }
.toggle { display: inline-block; width: 34px; height: 16px; border-radius: 8px;
  background: var(--accent); vertical-align: middle; position: relative; margin-right: 6px; }
.toggle::after { content: ""; position: absolute; right: 2px; top: 2px;
  width: 12px; height: 12px; border-radius: 50%%; background: #fff; }
.toggle.off { background: #A9B3BF; }
.toggle.off::after { left: 2px; right: auto; }
.btns { display: flex; gap: 8px; margin-top: 14px; }
.btn { padding: 5px 14px; border: 1px solid #9AA6B4; border-radius: 3px;
  font-size: 13px; background: #fff; }
.btn.p { background: var(--accent); border-color: var(--accent); color: #fff; }
table { border-collapse: collapse; width: 100%%; font-size: 12.5px; }
th { text-align: left; font-weight: 600; color: #4A5668; padding: 6px 8px;
  border-bottom: 1px solid #C9D1DB; white-space: nowrap; }
td { overflow-wrap: anywhere; padding: 6px 8px; border-bottom: 1px solid #EEF1F5; vertical-align: top; }
tr.sel td { background: #EEF4FC; }
.ok { color: #1E7A47; font-weight: 600; } .bad { color: #B42318; font-weight: 600; }
.warn { color: #8A5A00; font-weight: 600; }
.note { font-size: 12px; color: #4A5668; background: #F4F7FA;
  border-left: 3px solid var(--accent); padding: 7px 10px; margin: 10px 0; }
.mono, pre, code { font-family: 'Cascadia Code', Consolas, monospace; }
pre { margin: 0; white-space: pre-wrap; word-break: break-word; }
.term { background: #1E1E1E; color: #D4D4D4; padding: 12px 14px 14px;
  font-size: 12.5px; line-height: 1.5; }
.term .p { color: #6CC4FF; } .term .g { color: #7BD88F; } .term .y { color: #E5C07B; }
.term .r { color: #F28B82; } .term .d { color: #8B949E; }
.editor { display: flex; background: #1F1F1F; color: #D4D4D4; font-size: 12.5px; }
.editor .side { width: 150px; background: #252526; padding: 8px 0; font-size: 12px;
  color: #BBBBBB; font-family: Inter, sans-serif; }
.editor .side div { padding: 3px 12px; } .editor .side .on { background: #37373D; color: #fff; }
.editor .code { flex: 1; padding: 8px 0; min-width: 0; }
.editor .ln { display: flex; } .editor .n { width: 38px; text-align: right;
  padding-right: 12px; color: #6E7681; flex: none; }
.editor .k { color: #569CD6; } .editor .s { color: #CE9178; } .editor .c { color: #6A9955; }
.editor .h { color: #DCDCAA; } .editor .lnk { color: #9CDCFE; }
.grid { border-collapse: collapse; font-size: 12.5px; width: 100%%; }
.grid td, .grid th { border: 1px solid #D5DCE4; padding: 4px 7px; height: 24px; }
.grid th { background: #F1F3F5; color: #5B6675; font-weight: 500; text-align: center; }
.grid td.rh { background: #F1F3F5; color: #5B6675; text-align: center; width: 34px; }
.grid .hdr { background: #1E5E3B; color: #fff; font-weight: 600; }
.grid .num { text-align: right; }
.grid .lock { background: #F3F4F6; color: #5B6675; }
.grid .edit { background: #FFFBEA; }
.formula { display: flex; gap: 8px; align-items: center; padding: 5px 10px;
  border-bottom: 1px solid #D5DCE4; font-size: 12.5px; }
.formula .box { border: 1px solid #C9D1DB; padding: 2px 8px; min-width: 60px; }
.sheets { display: flex; gap: 2px; padding: 4px 10px; background: #F1F3F5;
  border-top: 1px solid #D5DCE4; font-size: 12px; }
.sheets div { padding: 3px 12px; } .sheets .on { background: #fff;
  border-bottom: 2px solid #1E7A47; font-weight: 600; }
.cards { display: grid; grid-template-columns: repeat(var(--n), 1fr); gap: 10px; margin: 6px 0 12px; }
.card { border: 1px solid #D5DCE4; border-radius: 6px; padding: 9px 12px; }
.card .k { font-size: 11.5px; color: #4A5668; }
.card .v { font-size: 20px; font-weight: 600; margin-top: 2px; }
.bars { display: flex; align-items: flex-end; gap: 6px; height: 90px;
  border-bottom: 1px solid #C9D1DB; padding: 0 4px; }
.bars div { flex: 1; background: var(--accent); opacity: .8; border-radius: 2px 2px 0 0; }
.bars .alt { background: #B42318; }
.axis { display: flex; gap: 6px; padding: 2px 4px 0; font-size: 10.5px; color: #6B7686; }
.axis div { flex: 1; text-align: center; }
.panel { border: 1px solid #D5DCE4; border-radius: 6px; padding: 10px 12px; margin-bottom: 10px; }
.panel .pt { font-weight: 600; font-size: 13px; margin-bottom: 6px; }
.two { display: grid; grid-template-columns: minmax(0,1fr) minmax(0,1fr); gap: 10px; }
.two .fields { grid-template-columns: minmax(90px, 150px) minmax(0,1fr); }
.v { overflow-wrap: anywhere; }
.gantt { position: relative; height: 22px; background: #F4F7FA; border-radius: 3px; margin: 3px 0; }
.gantt span { position: absolute; top: 3px; height: 16px; border-radius: 3px; background: var(--accent); opacity: .85; }
.gantt span.alt { background: #B42318; }
.chip { display: inline-block; padding: 1px 8px; border-radius: 10px; font-size: 11.5px;
  font-weight: 600; background: #E6EEF9; color: #1B4A8C; }
.chip.ok { background: #E3F4EA; color: #1E7A47; } .chip.bad { background: #FDE8E7; color: #B42318; }
.chip.warn { background: #FFF3D6; color: #8A5A00; }
.dlg { margin: 18px auto; width: 640px; max-width: calc(100%% - 24px); border: 1px solid #9AA6B4; border-radius: 4px;
  box-shadow: 0 6px 20px rgba(0,0,0,.18); background: #fff; }
.dlg .dt { background: #E9EDF2; padding: 6px 12px; font-size: 12.5px; font-weight: 600;
  border-bottom: 1px solid #C9D1DB; }
.dlg .db { padding: 12px 16px 14px; }
.backdrop { background: #DDE3EA; padding: 4px 0 8px; }
.mk { display: inline-flex; align-items: center; justify-content: center;
  width: 19px; height: 19px; border-radius: 50%%; background: #C2410C; color: #fff;
  font: 700 11.5px/1 Inter, sans-serif; margin: 0 3px; vertical-align: middle;
  box-shadow: 0 0 0 2px #fff; flex: none; }
.term .mk, .editor .mk { box-shadow: 0 0 0 2px #1E1E1E; }
"""


def esc(text):
    """Escape text and turn {n} into callout badges."""
    out = html.escape(str(text))
    return re.sub(r"\{(\d)\}", r'<span class="mk">\1</span>', out)


def raw(text):
    """Like esc() but leaves trusted inline HTML in place."""
    return re.sub(r"\{(\d)\}", r'<span class="mk">\1</span>', str(text))


# Components ---------------------------------------------------------------

def h1(t): return f"<h1>{esc(t)}</h1>"
def h2(t): return f"<h2>{esc(t)}</h2>"
def note(t): return f'<div class="note">{esc(t)}</div>'
def cmds(*items): return '<div class="cmds">' + "".join(f"<div>{esc(i)}</div>" for i in items) + "</div>"


def tabs(*items):
    """Items starting with * are the selected tab."""
    return '<div class="tabs">' + "".join(
        f'<div class="on">{esc(i[1:])}</div>' if i.startswith("*")
        else f"<div>{esc(i)}</div>" for i in items) + "</div>"


def fields(*rows):
    """(label, value) or (label, value, 'ro'). Values may use toggle()."""
    out = ['<div class="fields">']
    for r in rows:
        label, value = r[0], r[1]
        cls = "v ro" if len(r) > 2 and r[2] == "ro" else "v"
        out.append(f'<div class="l">{esc(label)}</div><div class="{cls}">{raw(value)}</div>')
    out.append("</div>")
    return "".join(out)


def toggle(on=True, text=""):
    return f'<span class="toggle{"" if on else " off"}"></span>{esc(text)}'


def buttons(*items):
    """Items starting with ! are primary."""
    return '<div class="btns">' + "".join(
        f'<div class="btn p">{esc(i[1:])}</div>' if i.startswith("!")
        else f'<div class="btn">{esc(i)}</div>' for i in items) + "</div>"


def table(headers, rows, selected=None, html_cells=False):
    f = raw if html_cells else esc
    out = ["<table><tr>", "".join(f"<th>{esc(h)}</th>" for h in headers), "</tr>"]
    for i, row in enumerate(rows):
        cls = ' class="sel"' if selected == i else ""
        out.append(f"<tr{cls}>" + "".join(f"<td>{f(c)}</td>" for c in row) + "</tr>")
    out.append("</table>")
    return "".join(out)


def chip(text, kind=""):
    return f'<span class="chip {kind}">{esc(text)}</span>'


def cards(*items):
    """(key, value)"""
    return (f'<div class="cards" style="--n:{len(items)}">' + "".join(
        f'<div class="card"><div class="k">{esc(k)}</div><div class="v">{esc(v)}</div></div>'
        for k, v in items) + "</div>")


def bars(values, labels=None, alt=()):
    top = max(values) or 1
    b = "".join(f'<div class="{"alt" if i in alt else ""}" style="height:{max(4, v / top * 100):.0f}%"></div>'
                for i, v in enumerate(values))
    out = f'<div class="bars">{b}</div>'
    if labels:
        out += '<div class="axis">' + "".join(f"<div>{esc(l)}</div>" for l in labels) + "</div>"
    return out


def panel(title, body):
    return f'<div class="panel"><div class="pt">{esc(title)}</div>{raw(body)}</div>'


def two(a, b): return f'<div class="two"><div>{a}</div><div>{b}</div></div>'


def gantt(rows):
    """rows: (label, start%, width%, alt)"""
    out = []
    for label, s, w, alt in rows:
        out.append(f'<div style="display:flex;gap:8px;align-items:center;font-size:12px">'
                   f'<div style="width:210px">{esc(label)}</div><div class="gantt" style="flex:1">'
                   f'<span class="{"alt" if alt else ""}" style="left:{s}%;width:{w}%"></span></div></div>')
    return "".join(out)


def term(text):
    """Terminal text. [p]..[/p] etc. colour spans; {n} markers."""
    t = esc(text)
    t = re.sub(r"\[(p|g|y|r|d)\](.*?)\[/\1\]", r'<span class="\1">\2</span>', t)
    return f'<div class="term"><pre>{t}</pre></div>'


def editor(files, lines, active=0, side=150):
    width = side
    side = "".join(f'<div class="{"on" if i == active else ""}"><pre>{esc(f)}</pre></div>'
                   for i, f in enumerate(files))
    body = []
    for n, line in enumerate(lines, 1):
        t = esc(line)
        t = re.sub(r"\[(k|s|c|h|lnk)\](.*?)\[/\1\]", r'<span class="\1">\2</span>', t)
        body.append(f'<div class="ln"><div class="n">{n}</div><pre>{t}</pre></div>')
    return (f'<div class="editor"><div class="side" style="width:{width}px">{side}</div>'
            f'<div class="code mono">{"".join(body)}</div></div>')


def grid(cols, rows, cell_class=None, sheet_tabs=(), active_sheet=0, formula=("A1", "")):
    """Spreadsheet grid. rows: list of lists; cell_class(r, c) -> css class."""
    out = [f'<div class="formula"><div class="box">{esc(formula[0])}</div><div>fx</div>'
           f'<div style="flex:1">{esc(formula[1])}</div></div>',
           '<table class="grid"><tr><th></th>' + "".join(f"<th>{c}</th>" for c in cols) + "</tr>"]
    for r, row in enumerate(rows, 1):
        cells = []
        for c, v in enumerate(row):
            cls = cell_class(r, c) if cell_class else ""
            cells.append(f'<td class="{cls}">{esc(v)}</td>')
        out.append(f'<tr><td class="rh">{r}</td>' + "".join(cells) + "</tr>")
    out.append("</table>")
    if sheet_tabs:
        out.append('<div class="sheets">' + "".join(
            f'<div class="{"on" if i == active_sheet else ""}">{esc(s)}</div>'
            for i, s in enumerate(sheet_tabs)) + "</div>")
    return "".join(out)


def dialog(title, body):
    return f'<div class="backdrop"><div class="dlg"><div class="dt">{esc(title)}</div><div class="db">{body}</div></div></div>'


# Frames -------------------------------------------------------------------

def frame(app, theme, body, crumb=None, nav=None, top=True, search="Search",
          who="dev@harbourline.example"):
    bg, fg, accent = THEMES[theme]
    parts = [f'<div class="win" style="--accent:{accent}">',
             '<div class="titlebar"><div class="dots"><span></span><span></span><span></span></div>'
             f'<div class="t">{esc(app)}</div><div class="tag">Illustrative mock-up</div></div>']
    if top:
        parts.append(f'<div class="top" style="background:{bg};color:{fg}"><div>{esc(app)}</div>'
                     + (f'<div class="search">{esc(search)}</div>' if search else "")
                     + f'<div class="who">{esc(who)}</div></div>')
    if crumb:
        segs = crumb.split(" › ")
        parts.append('<div class="crumb">' + " › ".join(esc(s) for s in segs[:-1])
                     + (" › " if len(segs) > 1 else "") + f"<b>{esc(segs[-1])}</b></div>")
    if nav:
        items = []
        for n in nav:
            if n.startswith("#"):
                items.append(f'<div class="h">{esc(n[1:])}</div>')
            elif n.startswith("*"):
                items.append(f'<div class="on">{esc(n[1:])}</div>')
            else:
                items.append(f"<div>{esc(n)}</div>")
        parts.append(f'<div class="layout"><div class="nav">{"".join(items)}</div>'
                     f'<div class="main">{body}</div></div>')
    else:
        parts.append(f'<div class="main">{body}</div>' if top else body)
    parts.append("</div>")
    return "".join(parts)


FONT_DIR = None  # set by the generator: folder holding cascadia-code-*.woff2


def page(content):
    faces = "".join(
        f"@font-face {{ font-family: 'Cascadia Code'; font-weight: {w}; "
        f"src: url('file://{FONT_DIR}/cascadia-code-latin-{w}-normal.woff2'); }}"
        for w in (400, 600)) if FONT_DIR else ""
    return (f"<!DOCTYPE html><html><head><meta charset='utf-8'><style>{faces}{CSS % {'w': WIDTH}}</style>"
            f"</head><body>{content}</body></html>")
