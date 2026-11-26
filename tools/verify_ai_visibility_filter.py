#!/usr/bin/env python3
"""Execute original LE:1 2055C..205E6 candidate filter under Unicorn.

The candidate object and template are synthetic. No external calls are stubbed.
The digest records selected ESI, accumulated score, and best distance.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EDI, UC_X86_REG_ESI
from analyze_executable import analyze
from verify_texts import TextsOracle


class CandidateOracle(TextsOracle):
    POOL = TextsOracle.BANK + 0x10000
    TEMPLATE = TextsOracle.BANK + 0x20000

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not 0x2055c <= at < 0x205e7:
            raise ValueError(f'Unexpected candidate-filter control flow {at:X}')

    def run_candidate(self, case):
        visibility, mask, flags, owner, hostile, hp, min_hp, x, y, max_distance, score = case
        cpu = self.cpu
        rec = bytearray(0x60)
        rec[3] = owner; rec[0x19] = hp; rec[0x20] = visibility
        struct.pack_into('<II', rec, 4, x, y)
        cpu.mem_write(self.POOL + 0x100, bytes(rec))
        tpl = bytearray(0xab)
        struct.pack_into('<I', tpl, 0x40, flags)
        tpl[0x46] = min_hp; tpl[0x76] = score
        cpu.mem_write(self.TEMPLATE, bytes(tpl))
        cpu.mem_write(0x6aa, struct.pack('<I', self.POOL))
        cpu.mem_write(0x6ce, struct.pack('<I', self.TEMPLATE))
        cpu.mem_write(0x1ca1, bytes([mask]))
        cpu.mem_write(0x1ca2, struct.pack('<I', hostile))
        cpu.mem_write(0x1f31, struct.pack('<III', 320, 320, max_distance))
        cpu.mem_write(0x1f3d, struct.pack('<I', 0))
        cpu.reg_write(UC_X86_REG_EDI, 0x100)
        cpu.reg_write(UC_X86_REG_ESI, 0)
        self.run('candidate', 0x2055c)
        selected = int(cpu.reg_read(UC_X86_REG_ESI) == self.POOL + 0x100)
        best, total = struct.unpack('<II', cpu.mem_read(0x1f39, 8))
        return [selected, best, total]


def cases():
    out=[]
    for visibility in [0,1,2,3,255]:
        for mask in [0,1,2,3,255]:
            for flags in [0,0x20,0x8000,0x400000]:
                for owner in [0,1,7,8]:
                    for hp,min_hp in [(0,0),(1,0),(9,8),(8,8),(200,100)]:
                        out.append([visibility,mask,flags,owner,1<<owner,hp,min_hp,
                                    320+(owner-2)*16,320+(hp%3)*16,1000,owner+1])
    return out


def generate(game):
    report, code = analyze(game)
    oracle = CandidateOracle(code)
    vectors = cases(); digest=hashlib.sha256()
    accepted = 0
    for case in vectors:
        result=oracle.run_candidate(case)
        digest.update(struct.pack('<III',*result));accepted+=result[0]
    return dict(format_version=1,source_sha256=report['source_sha256']['NORWIND.EXE'],
                case_count=len(vectors),accepted=accepted,results_sha256=digest.hexdigest(),
                dimensions=dict(visibility=[0,1,2,3,255],mask=[0,1,2,3,255],
                                flags=[0,0x20,0x8000,0x400000],owner=[0,1,7,8],
                                hp_min_hp=[[0,0],[1,0],[9,8],[8,8],[200,100]]))


if __name__ == '__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game',type=Path,required=True)
    ap.add_argument('--fixture',type=Path,required=True)
    args=ap.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game),indent=2)+'\n')
