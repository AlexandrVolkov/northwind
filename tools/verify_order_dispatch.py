#!/usr/bin/env python3
"""Execute native 61C3 command activation, stopping at each mode's destination.

Destination implementations and earlier UI event filtering are outside this boundary.
Unknown modes execute the original RET. No calls are stubbed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EIP, UC_X86_REG_ESP, UC_X86_REG_EFLAGS
from analyze_executable import analyze
from verify_texts import TextsOracle
from order_buttons_source import convert


class OrderOracle(TextsOracle):
    TARGETS = {0x622f, 0x4d41, 0x4e27, 0x4fce, 0x50e5, 0x51ac, 0x5297, 0x538d, 0x53c4, 0x54b4, 0x5593}

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if at in self.TARGETS:
            self.goal = at; cpu.emu_stop(); return
        if not 0x61c3 <= at < 0x622f: raise ValueError(f'Unexpected command activation escape: {at:X}')


class WorkTableOracle(TextsOracle):
    WRK = TextsOracle.BANK + 0x10000

    def guard(self, cpu, address, size, user_data):
        if address in self.targets:
            self.goal = address - self.WRK; cpu.emu_stop(); return
        if self.mode == 'gate' and address == self.WRK + 0xdc3:
            # FS:8D / CA49 is the gate's preceding common update, outside the switch boundary.
            cpu.reg_write(UC_X86_REG_EIP, self.WRK + 0xdc9); return
        if self.mode == 'gate': allowed = [(self.WRK + 0xd9c, self.WRK + 0xe7b)]
        else: allowed = [(self.CODE + 0x8227, self.CODE + 0x8270), (self.CODE + 0xc88c, self.CODE + 0xc8e1)]
        if not any(lo <= address < hi for lo, hi in allowed): raise ValueError(f'Unexpected WRK switch escape: {address:X}')


def verify_tables(game, code, catalog):
    o = WorkTableOracle(code); c = o.cpu
    c.mem_write(o.WRK, (game / 'ARMY__02.WRK').read_bytes())
    cases = []
    for template in catalog['templates']:
        dispatcher = template['dispatcher']
        if not dispatcher: continue
        o.targets = {o.WRK + state['wrk_target'] for state in template['states']}
        for state in range(6):
            record = bytearray(64); record[0x14] = state; record[0x19] = 100
            c.mem_write(o.BANK, bytes(record)); c.mem_write(o.BANK + 0x1000, bytes(0x45) + b'\x64')
            c.mem_write(0x1c49, struct.pack('<I', o.BANK + 0x1000)); c.reg_write(UC_X86_REG_ESI, o.BANK)
            if dispatcher.get('kind') == 'custom_gate': o.run('gate', o.WRK - o.CODE + dispatcher['wrk_entry'])
            else:
                o.mode = 'table'; o.goal = None
                c.mem_write(o.STACK + 0xf00, struct.pack('<I', o.WRK + dispatcher['wrk_table']))
                c.reg_write(UC_X86_REG_ESP, o.STACK + 0xf00); c.reg_write(UC_X86_REG_EFLAGS, 2)
                c.emu_start(o.CODE + dispatcher['le1'], o.STOP, count=1000)
            expected = template['states'][state]['wrk_target']
            if o.goal != expected: raise ValueError(f'Native WRK target disagrees for template {template["template_index"]}, state {state}')
            cases.append([template['template_index'], state, o.goal])
    return cases


def generate(game):
    report, code = analyze(game); o = OrderOracle(code); c = o.cpu; digest = hashlib.sha256()
    routes = {}
    for slot in range(6):
        for mode in range(256):
            widget = bytearray(b'\x5a' * 32); widget[2] = slot; widget[0x1a] = 0
            c.mem_write(o.BANK, bytes(widget)); c.mem_write(o.BANK + 256, bytes(6) + bytes([mode, 0]))
            c.mem_write(0x19c0, b'\xa5')
            c.reg_write(UC_X86_REG_ESI, o.BANK); c.reg_write(UC_X86_REG_EDI, o.BANK + 256)
            o.run('activation', 0x61c3)
            digest.update(bytes(c.mem_read(o.BANK, 32))); digest.update(bytes(c.mem_read(0x19c0, 1)))
            digest.update(struct.pack('<I', o.goal or 0))
            if o.goal: routes[str(mode)] = o.goal
    catalog = convert(game)
    return dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'], slots=6, modes=256,
                case_count=1536, sha256=digest.hexdigest(), destinations=routes, wrk_source_sha256=catalog['source_sha256'],
                wrk_targets=verify_tables(game, code, catalog),
                table_boundary='Stops at real WRK state handler entry. Gate preceding FS:8D is stubbed; handlers are not executed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args(); result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n')
    print(f'Native activation: {result["case_count"]} cases, {len(result["destinations"])} mode routes')
