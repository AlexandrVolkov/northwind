#!/usr/bin/env python3
"""Decode original EPI campaign layout files into portable JSON."""
import argparse
import hashlib
import json
from pathlib import Path


def parse_episode(raw: bytes) -> dict:
    if len(raw) < 6 + 16 * 22 + 5:
        raise ValueError('EPI header is incomplete')
    width, height = raw[0], raw[1]
    if not 1 <= width <= 16 or not 1 <= height <= 16:
        raise ValueError('EPI world dimensions are invalid')
    preload_count = raw[4]
    expected = 6 + 16 * 22 + width * height + preload_count
    if len(raw) != expected:
        raise ValueError(f'EPI length {len(raw)} differs from {expected}')
    people = []
    for index in range(16):
        entry = raw[6 + index * 22:6 + (index + 1) * 22]
        name = entry[:20].split(b'\xff')[0].split(b'\x00')[0].decode('cp866')
        people.append(dict(index=index, name=name, position=list(entry[20:22])))
    grid = raw[358:358 + width * height]
    return dict(width=width, height=height, header_tail=list(raw[2:6]),
                preload_count=preload_count, participant_count=raw[5],
                people=people, area_ids=[list(grid[row * width:(row + 1) * width]) for row in range(height)],
                preload_ids=list(raw[358 + width * height:]))


def export(game: Path, output: Path) -> dict:
    episodes = {}
    for filename in ('GAME.EPI', 'DEMO.EPI', 'DEMO2.EPI'):
        raw = (game / filename).read_bytes()
        episodes[filename] = dict(source_sha256=hashlib.sha256(raw).hexdigest(), **parse_episode(raw))
    result = dict(format_version=1, source='original EPI', episodes=episodes)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=Path('data/original_episodes.json'))
    args = parser.parse_args()
    print(f'Exported {len(export(args.game, args.output)["episodes"])} EPI layouts')
