#!/usr/bin/env python3
"""Index DAT-to-WRK entry points from tracked metadata; no ISO or executable required."""
import argparse
from collections import defaultdict
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def render(templates, analysis):
    profiles = {a['dat']: a for a in analysis['armies']}
    if set(profiles) != set(templates['armies']):
        raise ValueError('Imported armies do not match the executable analysis profile')
    sections = []; total = entries = 0
    for dat, army in sorted(templates['armies'].items()):
        profile = profiles[dat]; callbacks = {c['template_index']: c for c in profile['callbacks']}
        if len(callbacks) != len(army['templates']) or profile['template_count'] != len(callbacks):
            raise ValueError(f'Template count mismatch: {dat}')
        groups = defaultdict(list)
        for t in army['templates']:
            callback = callbacks[t['template_index']]; raw = bytes.fromhex(t['raw_record_hex'])
            if (len(raw) != 171 or int.from_bytes(raw[0x3b:0x3f], 'little') != t['wrk_entry_offset'] or
                callback['wrk_offset'] != t['wrk_entry_offset'] or callback['name'] != t['name'] or
                callback['dat_record_offset'] != t['file_offset']):
                raise ValueError(f'DAT/WRK profile mismatch: {dat}[{t["template_index"]}]')
            groups[t['wrk_entry_offset']].append(t)
        total += len(army['templates']); entries += len(groups)
        sections += [f'## {profile["wrk"]}', '', f'Шаблонов: {len(army["templates"])}. Точек входа: {len(groups)}.', '',
                     '| Смещение WRK | Шаблоны DAT: индекс и имя | Размеры runtime-записи, байт |', '| --- | --- | --- |']
        for offset, members in sorted(groups.items()):
            names = '; '.join(f'{t["template_index"]}: {t["name"]}'.replace('|', '\\|') for t in members)
            sizes = ', '.join(str(v) for v in sorted({t['runtime_record_bytes'] for t in members}))
            sections.append(f'| `0x{offset:04X}` | {names} | {sizes} |')
        sections.append('')
    header = ['# Индекс обработчиков объектов', '',
              f'**{total} шаблона, {entries} отдельных пар WRK + смещение.** Это точки входа, а не количество всех функций игры или оценка готовности переноса.', '',
              'Сгенерировано `tools/catalog_game_logic.py` из включённых в Git `assets/imported/templates.json` и `docs/code-analysis.json`. '
              'Индекс проверяет имя, смещение записи, указатель WRK в необработанной записи DAT и совпадение с профилем анализа. '
              'Он не декомпилирует тела функций и не доказывает смысл обработчиков.', '',
              f'Профиль NORWIND.EXE: `{analysis["source_sha256"]["NORWIND.EXE"]}`.', '',
              'Общее устройство и статусы: [game-logic-map.md](game-logic-map.md). '
              'Совпавшее смещение внутри одного WRK означает общий вход; одинаковые числа в разных WRK не означают одинаковый код. '
              'У одинаковых имён могут быть разные обработчики. Подпрограммы, общие хвосты и параметры DAT требуют отдельного разбора.', '']
    return '\n'.join(header + sections), total, entries


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, default=ROOT / 'docs/logic-handlers.md')
    p.add_argument('--check', action='store_true', help='Check the existing index without writing')
    a = p.parse_args()
    text, templates, entries = render(json.loads((ROOT / 'assets/imported/templates.json').read_text(encoding='utf-8')),
                                     json.loads((ROOT / 'docs/code-analysis.json').read_text(encoding='utf-8')))
    if a.check:
        if not a.output.exists() or a.output.read_text(encoding='utf-8') != text:
            raise SystemExit('Logic index is missing or stale; regenerate it')
    else:
        a.output.write_text(text, encoding='utf-8')
    print(f'Logic index: {templates} templates, {entries} WRK entry points; {"verified" if a.check else "written"}')


if __name__ == '__main__':
    main()
