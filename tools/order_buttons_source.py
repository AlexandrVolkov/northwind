#!/usr/bin/env python3
"""Export actual DAT command descriptions and WRK state-dispatch addresses.

The DAT dword is a tooltip pointer, not a WRK callback. WRK callbacks come from
DAT[3B] and the unit/building six-state dispatcher, or the gate's custom branch.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from analyze_executable import analyze, EXE_SHA256
from import_game import decode_templates

PROFILE = {'NORWIND.EXE': EXE_SHA256,
           'ARMY__02.DAT': 'f3e67bae10d1042689e13802fadba443329cc782c612208792d4eee00fb72475',
           'ARMY__02.WRK': '71744179572a6958a7691a5c62ceca709e1bce6c4d3303fb407ad03280251b0b'}


def mode_targets(code):
    result, pending = {}, None
    for ins in Cs(CS_ARCH_X86, CS_MODE_32).disasm(code[0x61d4:0x622f], 0x61d4):
        if ins.mnemonic == 'test' and ins.op_str == 'al, al': pending = 0
        elif ins.mnemonic == 'cmp' and ins.op_str.startswith('al, '): pending = int(ins.op_str[4:], 0)
        elif ins.mnemonic == 'je' and pending is not None:
            result[pending] = int(ins.op_str, 0); pending = None
    if set(result) != set(range(10)) | {11}: raise ValueError('Unexpected DAT mode router')
    return result


def convert(game):
    for name, digest in PROFILE.items():
        if hashlib.sha256((game / name).read_bytes()).hexdigest() != digest: raise ValueError(f'Unknown source profile: {name}')
    report, code = analyze(game); routes = mode_targets(code)
    fs = {item['fs_slot']: item['target_offset'] for item in report['fs_assignments']}
    templates = decode_templates((game / 'ARMY__02.DAT').read_bytes())['templates']
    wrk = (game / 'ARMY__02.WRK').read_bytes()
    ends = sorted({t['wrk_entry_offset'] for t in templates}) + [len(wrk)]
    result = []
    for template in templates:
        entry = template['wrk_entry_offset']; end = next(at for at in ends if at > entry)
        candidates = []
        for slot in [0xe1, 0x41]:
            signature = b'\x64\x67\xff\x16' + struct.pack('<H', slot)
            at = wrk.find(signature, entry, end)
            if at >= 0: candidates.append((at, slot))
        if len(candidates) > 1: raise ValueError('Ambiguous WRK dispatcher')
        dispatcher = None; states = []
        if candidates:
            at, slot = candidates[0]; base = at + 6
            targets = [base + value for value in struct.unpack_from('<6I', wrk, base)]
            if any(not entry <= target < end for target in targets): raise ValueError('WRK state target outside callback region')
            dispatcher = dict(wrk_call=at, wrk_table=base, fs_slot=slot, le1=fs[slot])
        elif template['template_index'] == 14:
            # Gate D9C..E7B: an explicit switch, not a six-entry inline table.
            targets = [0xe21, 0xde4, 0xe22, 0xe34, 0xe4c, 0xde4]
            dispatcher = dict(kind='custom_gate', le1=None, wrk_entry=0xd9c)
        else: targets = []
        for index, target in enumerate(targets):
            ins = next(Cs(CS_ARCH_X86, CS_MODE_32).disasm(wrk[target:target + 16], target))
            transfer = re.fullmatch(r'dword ptr fs:\[(0x[0-9a-f]+)\]', ins.op_str)
            state = dict(state=index, wrk_target=target)
            if ins.mnemonic in ['call', 'jmp'] and transfer:
                slot = int(transfer[1], 16)
                state['first_transfer'] = dict(kind=ins.mnemonic, fs_slot=slot, le1=fs[slot])
            states.append(state)
        commands = []
        for command in template['commands']:
            commands.append({key: command[key] for key in ['slot', 'icon_frame', 'mode', 'raw_group', 'present', 'label', 'hotkey_scan_code']})
            commands[-1]['tooltip_offset'] = command.get('tooltip_offset')
            commands[-1]['mode_target'] = routes.get(command['mode'])
            commands[-1]['queued_state'] = command['slot'] if command['present'] and command['mode'] == 0 else None
        result.append(dict(template_index=template['template_index'], name=template['name'], wrk_entry=entry,
                           dispatcher=dispatcher, states=states, commands=commands))
    return dict(format_version=1, source_sha256=PROFILE, mode_targets=routes, templates=result,
                limitations=['State targets are entry addresses, not a claim that their complete handlers have been ported.',
                             'A command icon is not a handler ID. DAT mode routing happens before WRK state dispatch.'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('data/original_order_buttons.json'))
    args = parser.parse_args(); result = convert(args.game)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(f'Exported {len(result["templates"])} templates and {len(result["mode_targets"])} mode routes')
