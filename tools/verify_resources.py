#!/usr/bin/env python3
"""Execute SCN stock-copy instructions and recruitment debit against original x86.

Stock copy stops at 2F1B before unrelated slot fields. Recruitment stops at A90F
before producer-state mutation, or AA5D before retry state. No allocation stubs.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_ECX, UC_X86_REG_EIP
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class ResourceOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        if self.mode == 'stock':
            if not self.CODE + 0x2f03 <= address < self.CODE + 0x2f1b:
                raise ValueError(f'Unexpected stock-copy escape {address:#x}')
        elif address == self.CODE + 0xaa5d:
            self.rejected = True
            cpu.emu_stop()
        elif not self.CODE + 0xa8bb <= address < self.CODE + 0xa90f:
            raise ValueError(f'Unexpected recruitment-payment escape {address:#x}')

    def stocks(self, raw):
        self.mode = 'stock'
        self.cpu.mem_write(self.POOL, raw)
        self.cpu.mem_write(self.PARTICIPANTS, b'\xa5' * 0xf5)
        self.cpu.reg_write(UC_X86_REG_ESI, self.POOL)
        self.cpu.reg_write(UC_X86_REG_EDI, self.PARTICIPANTS)
        self.cpu.emu_start(self.CODE + 0x2f03, self.CODE + 0x2f1b, count=100)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.CODE + 0x2f1b:
            raise ValueError('Stock copy did not reach its exact end')
        owner = bytes(self.cpu.mem_read(self.PARTICIPANTS, 0xf5))
        if owner[:0x18] != b'\xa5' * 0x18 or owner[0x28:] != b'\xa5' * (0xf5-0x28):
            raise ValueError('Stock copy overwrote other owner fields')
        return owner[0x18:0x28]

    def payment(self, costs, balances):
        self.mode = 'payment'; self.rejected = False
        self.cpu.mem_write(self.DAT + 0x7c, struct.pack('<4I', *costs))
        self.cpu.mem_write(self.PARTICIPANTS, b'\xa5' * 0xf5)
        self.cpu.mem_write(self.PARTICIPANTS + 0x18, struct.pack('<4I', *balances))
        self.cpu.reg_write(UC_X86_REG_EDI, self.DAT)
        self.cpu.reg_write(UC_X86_REG_ECX, self.PARTICIPANTS)
        self.cpu.emu_start(self.CODE + 0xa8bb, self.CODE + 0xa90f, count=100)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.CODE + (0xaa5d if self.rejected else 0xa90f):
            raise ValueError('Payment did not reach an expected boundary')
        owner = bytes(self.cpu.mem_read(self.PARTICIPANTS, 0xf5))
        if owner[:0x18] != b'\xa5' * 0x18 or owner[0x28:] != b'\xa5' * (0xf5-0x28):
            raise ValueError('Payment overwrote unrelated owner fields')
        return bytes([not self.rejected]) + owner[0x18:0x28]


def generate(game, scenario, construction):
    report, code = analyze(game)
    oracle = ResourceOracle(code, report['fs_assignments'])
    raw_slots = [s['raw_record_hex'] for s in json.loads(scenario.read_text())['slots']]
    # Distinguishable adjacent slots and full uint32 boundaries, never inferred expected outputs.
    for values in [(1,2,3,4), (0xffffffff,0,0x80000000,0x7fffffff), (0,0,0,0)]:
        raw = bytearray([0xa5] * 114); struct.pack_into('<4I', raw, 0x15, *values)
        raw_slots.append(raw.hex())
    digest = hashlib.sha256()
    for raw in raw_slots: digest.update(oracle.stocks(bytes.fromhex(raw)))
    stocks = dict(raw_slots=raw_slots, case_count=len(raw_slots), sha256=digest.hexdigest())
    # Reuse all 32,805 construction-payment input cases to prove recruitment uses the same contract.
    reference = json.loads(construction.read_text())['payment']; digest = hashlib.sha256(); count = 0
    for costs in reference['costs']:
        for balances in product(reference['balance_values'], repeat=4):
            digest.update(oracle.payment(costs, balances)); count += 1
    payment = dict(costs=reference['costs'],balance_values=reference['balance_values'],case_count=count,sha256=digest.hexdigest())
    if payment['sha256'] != reference['sha256'] or count != reference['case_count']:
        raise ValueError('Recruitment and construction payments differ')
    return dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'], stocks=stocks, payment=payment)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--scenario',type=Path,default=Path('assets/imported/scenario.json'))
    parser.add_argument('--construction',type=Path,default=Path('tests/fixtures/construction.json'))
    parser.add_argument('--fixture',type=Path,required=True)
    args=parser.parse_args()
    result=generate(args.game,args.scenario,args.construction)
    args.fixture.write_text(json.dumps(result,indent=2)+'\n')
    print('SCN stocks',result['stocks']['case_count'],'recruitment payments',result['payment']['case_count'])
