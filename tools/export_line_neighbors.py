#!/usr/bin/env python3
"""Export the neighbour tables of the line templates (command AH=9 of their WRK entry, read by LE:1 BC2E): the mask byte
returned in AH and the pairs (frame, neighbour mask) up to the pair that starts with FFh. Only these few constants are written."""
import argparse
import json
from pathlib import Path
import re
import struct

STRIDE = 171


def export(game):
    dat = (game / 'ARMY__02.DAT').read_bytes(); wrk = (game / 'ARMY__02.WRK').read_bytes()
    out = {}
    for t in range((len(dat) - 2) // STRIDE):
        record = dat[2 + t * STRIDE:2 + (t + 1) * STRIDE]
        if not struct.unpack_from('<I', record, 0x40)[0] & 0x80: continue
        entry = struct.unpack_from('<I', record, 0x3b)[0]
        head = wrk[entry:entry + 0x40]
        # The entry compares AH with 9 and jumps (rel8) to the branch: mov edx,imm / call +0 / pop eax / add edx,eax / [mov ah,FFh] / mov ah,mask / ret.
        j = head.find(b'\x80\xfc\x09\x74')
        if j < 0: continue
        branch = entry + j + 5 + head[j + 4]
        m = re.match(rb'\xba(....)\xe8\x00\x00\x00\x00\x58\x03\xd0(?:\xb4\xff)?\xb4(.)\xc3', wrk[branch:branch + 0x20], re.S)
        if not m: continue
        table = struct.unpack('<i', m.group(1))[0] + branch + 10
        pairs = bytearray()
        while True:
            pairs += wrk[table:table + 2]; table += 2
            if wrk[table] == 0xff: break
        out[str(t)] = {'mask': m.group(2)[0], 'pairs_hex': pairs.hex()}
    return {'templates': out}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(export(args.game), indent=1) + '\n', encoding='utf-8')
