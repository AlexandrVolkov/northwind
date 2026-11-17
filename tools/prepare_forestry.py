#!/usr/bin/env python3
"""Prepare verified terrain constants. Development only; outputs are bundled."""
import argparse
import json
from pathlib import Path
from forestry_source import source_constants


def prepare(game,root):
    constants=source_constants(game)
    (root/'data/original_terrain.json').write_text(json.dumps(constants,indent=2)+'\n',encoding='utf-8')
    print('Prepared verified LE terrain constants')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('game',type=Path);args=parser.parse_args()
    prepare(args.game,Path(__file__).resolve().parents[1])
