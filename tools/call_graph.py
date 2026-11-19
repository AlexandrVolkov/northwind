#!/usr/bin/env python3
"""Call graph of an LE:1 code range of NORWIND.EXE with porting status from data/reverse_engineering/annotations.json.

Functions are found by recursive descent from the roots: conditional and unconditional jumps stay inside the function,
calls (direct and through LE:3 pointer tables such as F06A/F083) start new functions. A jump to a known function start
is a tail call. Functions inside the range are expanded; callees outside it are shown as leaves.

Outputs a Markdown list (function → size → status → callees) and a self-contained HTML page with a layered SVG graph.
No original bytes are written: only addresses, instruction counts and the names from the annotation registry.

  python tools/call_graph.py --game <dir with NORWIND.EXE> --name ai --start 1E53A --end 23DC2 --root 1E7EA \
      --markdown docs/ai-call-graph.md --html docs/ai-call-graph.html
"""
import argparse
import html
import json
import re
import struct
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from analyze_executable import read_le_objects, INNER_MZ

ROOT = Path(__file__).resolve().parents[1]
TABLE = re.compile(r'(?:call|jmp) dword ptr \[e[a-z]x\*4 \+ (0x[0-9a-f]+)\]')
DIRECT = re.compile(r'^(call|jmp|j[a-z]+) (0x[0-9a-f]+)$')


def load(game):
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    return objects[0]['payload'], objects[2]['payload']


def table_entries(data, at, code_size, starts):
    out = []
    while at + 4 <= len(data):
        value = struct.unpack_from('<I', data, at)[0]
        if not 0x100 <= value < code_size or (out and value in starts and False): break
        out.append(value); at += 4
        if len(out) >= 64: break
    return out


def discover(code, data, roots, lo, hi):
    decoder = Cs(CS_ARCH_X86, CS_MODE_32)
    functions, queue = {}, list(roots)
    known = set(roots)
    while queue:
        start = queue.pop()
        if start in functions: continue
        if not lo <= start < hi:
            functions[start] = {'size': 0, 'calls': set(), 'tables': {}, 'addresses': set()}
            continue
        seen, todo, calls, tables = set(), [start], set(), {}
        while todo:
            at = todo.pop()
            while at not in seen and lo <= at < hi:
                ins = next(decoder.disasm(code[at:at + 16], at), None)
                if ins is None: break
                seen.add(at); text = f'{ins.mnemonic} {ins.op_str}'.strip(); nxt = at + ins.size
                m = TABLE.search(text)
                if m:
                    entries = table_entries(data, int(m.group(1), 16), len(code), known)
                    tables[int(m.group(1), 16)] = entries; calls.update(entries)
                    if ins.mnemonic == 'jmp': break
                    at = nxt; continue
                m = DIRECT.match(text)
                if m:
                    kind, target = m.group(1), int(m.group(2), 16)
                    if kind == 'call': calls.add(target); at = nxt; continue
                    if kind == 'jmp':
                        if target in known and target != start: calls.add(target); break
                        at = target; continue
                    todo.append(target); at = nxt; continue
                if ins.mnemonic in ('ret', 'retf', 'iretd') or (ins.mnemonic == 'jmp'): break
                at = nxt
        functions[start] = {'size': len(seen), 'calls': calls, 'tables': tables, 'addresses': seen}
        for c in calls:
            known.add(c)
            if c not in functions: queue.append(c)
    # A function body may run into a later start found only afterwards: cut bodies at other starts.
    starts = sorted(functions)
    for f in starts:
        others = [s for s in starts if s > f]
        if others and functions[f]['addresses']:
            cut = {a for a in functions[f]['addresses'] if a >= f and not any(f < s <= a for s in others if s in functions[f]['addresses'])}
            functions[f]['size'] = len(cut); functions[f]['addresses'] = cut
    return functions


def annotations():
    regions = json.loads((ROOT / 'data/reverse_engineering/annotations.json').read_text(encoding='utf-8'))['regions']
    return [r for r in regions if r['module'] == 'LE:1']


def status(fn, regions, start):
    names = [r['name'] for r in regions if r['start'] <= start < r['end']]
    if not fn['addresses']:
        st = [r['status'] for r in regions if r['start'] <= start < r['end']]
        return (st[0] if st else 'unknown'), (names[0] if names else '')
    ported = sum(1 for a in fn['addresses'] if any(r['start'] <= a < r['end'] and r['status'] == 'ported' for r in regions))
    partial = sum(1 for a in fn['addresses'] if any(r['start'] <= a < r['end'] and r['status'] != 'ported' for r in regions))
    n = len(fn['addresses'])
    if ported == n: s = 'ported'
    elif ported + partial == 0: s = 'not ported'
    else: s = 'partial'
    return s, (names[0] if names else '')


COLORS = {'ported': '#2e9e5b', 'partial': '#d9a521', 'not ported': '#c8463c', 'unknown': '#8a8f98', 'external': '#5b7fbf'}


def render_html(title, functions, info, roots, lo, hi):
    # Layers by shortest call depth from the roots.
    depth = {r: 0 for r in roots}; frontier = list(roots)
    while frontier:
        nxt = []
        for f in frontier:
            for c in sorted(functions[f]['calls']):
                if c not in depth: depth[c] = depth[f] + 1; nxt.append(c)
        frontier = nxt
    layers = {}
    for f, d in depth.items(): layers.setdefault(d, []).append(f)
    W, H, X0, Y0, PER_ROW, ROW = 92, 34, 20, 30, 14, 52
    pos = {}; y = Y0; label_rows = []
    for d, fs in sorted(layers.items()):
        fs.sort(); label_rows.append((d, y, len(fs)))
        for i, f in enumerate(fs): pos[f] = (X0 + i % PER_ROW * W, y + 18 + i // PER_ROW * ROW)
        y += 18 + ((len(fs) + PER_ROW - 1) // PER_ROW) * ROW + 40
    width, height = PER_ROW * W + 2 * X0, y
    edges, nodes = [], []
    for f in pos:
        for c in functions[f]['calls']:
            if c in pos:
                (x1, y1), (x2, y2) = pos[f], pos[c]
                edges.append(f'<line class="e" data-a="{f:x}" data-b="{c:x}" x1="{x1+38}" y1="{y1+H}" x2="{x2+38}" y2="{y2}"/>')
    for d, ly, n in label_rows:
        nodes.append(f'<text class="l" x="{X0}" y="{ly + 8}">Глубина {d}: {n}</text>')
    for f, (x, y) in pos.items():
        st, name, size = info[f]
        color = COLORS['external'] if not lo <= f < hi and st in ('unknown', 'not ported') else COLORS.get(st, '#888')
        tip = html.escape(f'LE:1 {f:X} — {name or "без имени"} — {st} — {size} инстр. — вызывает: ' + ', '.join(f'{c:X}' for c in sorted(functions[f]['calls'])))
        nodes.append(f'<g class="n" data-id="{f:x}" transform="translate({x},{y})"><title>{tip}</title>'
                     f'<rect width="76" height="{H}" rx="5" fill="{color}"/><text x="38" y="14">{f:X}</text>'
                     f'<text x="38" y="27" class="s">{size}</text></g>')
    counts = {}
    total = {}
    for f, (st, _, size) in info.items():
        if lo <= f < hi: counts[st] = counts.get(st, 0) + 1; total[st] = total.get(st, 0) + size
    summary = ' · '.join(f'<span style="color:{COLORS.get(k,"#888")}">■</span> {k}: {counts[k]} процедур, {total[k]} инструкций' for k in sorted(counts))
    return f'''<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{html.escape(title)}</title><style>
:root{{--bg:#fbfaf7;--fg:#1d1f23;--muted:#6b7078;--edge:#b9bcc2;--hl:#1d1f23}}
@media (prefers-color-scheme:dark){{:root{{--bg:#16181c;--fg:#e8e6e1;--muted:#9aa0a8;--edge:#454a52;--hl:#f2f0ea}}}}
body{{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif}}
header{{padding:16px 16px 4px}} h1{{font-size:18px;margin:0 0 6px}} p{{margin:4px 0;color:var(--muted)}}
#wrap{{overflow:auto;padding:0 16px 16px}} svg{{display:block}}
.e{{stroke:var(--edge);stroke-width:1;opacity:.55}} .l{{fill:var(--muted);font:12px system-ui}} .e.on{{stroke:var(--hl);stroke-width:2;opacity:1}}
.n text{{fill:#fff;font:600 11px ui-monospace,monospace;text-anchor:middle}} .n text.s{{font-weight:400;font-size:10px}}
.n{{cursor:pointer}} .n.dim{{opacity:.25}} .n.sel rect{{stroke:var(--hl);stroke-width:3}}
</style></head><body><header><h1>{html.escape(title)}</h1>
<p>Узлы — процедуры LE:1 (адрес, число инструкций), слои — глубина вызова от корня. Наведите для имени и списка вызовов, щёлкните для подсветки связей.</p>
<p>{summary}</p></header><div id="wrap"><svg width="{width}" height="{height}">{"".join(edges)}{"".join(nodes)}</svg></div>
<script>
const nodes=[...document.querySelectorAll('.n')],edges=[...document.querySelectorAll('.e')];
nodes.forEach(n=>n.addEventListener('click',()=>{{const id=n.dataset.id,was=n.classList.contains('sel');
nodes.forEach(m=>m.classList.remove('sel','dim'));edges.forEach(e=>e.classList.remove('on'));if(was)return;
const near=new Set([id]);edges.forEach(e=>{{if(e.dataset.a===id||e.dataset.b===id){{e.classList.add('on');near.add(e.dataset.a);near.add(e.dataset.b)}}}});
nodes.forEach(m=>{{if(!near.has(m.dataset.id))m.classList.add('dim')}});n.classList.add('sel')}}));
</script></body></html>'''


def render_markdown(title, functions, info, lo, hi, args):
    lines = [f'# {title}', '', f'Сгенерировано `tools/call_graph.py` ({" ".join(args)}). Не правьте вручную: статусы берутся из '
             '`data/reverse_engineering/annotations.json`, процедуры — рекурсивным спуском по коду.', '']
    inside = [f for f in sorted(functions) if lo <= f < hi]
    by = {}
    for f in inside:
        st, _, size = info[f]; by.setdefault(st, [0, 0]); by[st][0] += 1; by[st][1] += size
    lines += ['| Статус | Процедур | Инструкций |', '|---|---|---|'] + [f'| {k} | {v[0]} | {v[1]} |' for k, v in sorted(by.items())] + ['']
    callers = {}
    for f, fn in functions.items():
        for c in fn['calls']: callers.setdefault(c, set()).add(f)
    lines += ['| Процедура | Инстр. | Статус | Имя | Вызывает (✓ перенесено, ~ частично, · нет) |', '|---|---|---|---|---|']
    mark = {'ported': '✓', 'partial': '~'}
    for f in inside:
        st, name, size = info[f]
        calls = ', '.join(f'{c:X}{mark.get(info[c][0], "·")}' for c in sorted(functions[f]['calls']))
        lines.append(f'| {f:X} | {size} | {st} | {name} | {calls} |')
    outside = sorted(c for c in functions if not lo <= c < hi)
    lines += ['', '## Вызываемое вне диапазона', '', '| Процедура | Статус | Имя | Кто вызывает |', '|---|---|---|---|']
    for c in outside:
        st, name, _ = info[c]
        lines.append(f'| {c:X} | {st} | {name} | {", ".join(f"{x:X}" for x in sorted(callers.get(c, [])))} |')
    return '\n'.join(lines) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--name', default='ai')
    parser.add_argument('--title', default='Граф вызовов ИИ поселения (LE:1)')
    parser.add_argument('--start', required=True); parser.add_argument('--end', required=True)
    parser.add_argument('--root', action='append', required=True)
    parser.add_argument('--markdown', type=Path); parser.add_argument('--html', type=Path)
    args = parser.parse_args()
    code, data = load(args.game)
    lo, hi = int(args.start, 16), int(args.end, 16); roots = [int(r, 16) for r in args.root]
    outside = [r for r in roots if not lo <= r < hi]
    if outside: print('warning: roots outside --start..--end are not expanded: ' + ', '.join(f'{r:X}' for r in outside))
    functions = discover(code, data, roots, lo, hi)
    regions = annotations()
    info = {}
    for f, fn in functions.items():
        st, name = status(fn, regions, f)
        info[f] = (st, name, fn['size'])
    shown = ['--start', args.start, '--end', args.end] + sum((['--root', r] for r in args.root), [])
    if args.markdown: args.markdown.write_text(render_markdown(args.title, functions, info, lo, hi, shown), encoding='utf-8')
    if args.html: args.html.write_text(render_html(args.title, functions, info, roots, lo, hi), encoding='utf-8')
    inside = [f for f in functions if lo <= f < hi]
    print(f'{len(inside)} functions in range, {len(functions) - len(inside)} outside')


if __name__ == '__main__':
    main()
