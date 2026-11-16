#!/usr/bin/env python3
"""Generate C# reference hashes by executing two original x86 routines.

Unlike verify_building_frames, this tool has no Python gameplay translation.
The golden outputs come directly from the original instructions. Neither
routine requires stubbing a call or loading DOS, graphics, or game assets.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import (UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBX,
                              UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS)
from analyze_executable import analyze


class OriginalKernel:
    CODE, OBJECT, TEMPLATE, STACK = 0x10000, 0x400000, 0x500000, 0x600000
    OWNER = 7
    FUNDS = 0x97a + OWNER * 0xf5 + 0x18
    NEIGHBOR = 0x97a + (OWNER-1) * 0xf5 + 0x18
    SENTINEL = b'\x87\x65\x43\x21' * 3
    STOP = STACK + 2048

    def __init__(self, code):
        self.cpu = Uc(UC_ARCH_X86, UC_MODE_32)
        self.cpu.mem_map(0, 0x10000)
        self.cpu.mem_map(self.CODE, (len(code)+4095)//4096*4096)
        self.cpu.mem_write(self.CODE, code)
        for address in [self.OBJECT, self.TEMPLATE, self.STACK]:
            self.cpu.mem_map(address, 4096)
        self.bounds = (0, 0)
        self.cpu.hook_add(UC_HOOK_CODE, self.guard)

    def guard(self, cpu, address, size, user_data):
        if not self.CODE+self.bounds[0] <= address < self.CODE+self.bounds[1]:
            raise ValueError(f'Unexpected control-flow escape: {address:#x}')

    def run(self, bounds, state, template, signal=0):
        self.bounds = bounds
        before = bytearray(b'\x5a' * 64)
        before[3] = self.OWNER
        before[0x19], before[0x16], before[0x0f] = state[:3]
        struct.pack_into('<H', before, 0x17, state[3])
        self.cpu.mem_write(self.OBJECT, bytes(before))
        self.cpu.mem_write(self.TEMPLATE, bytes(template))
        self.cpu.mem_write(self.STACK+4092, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESI, self.OBJECT)
        self.cpu.reg_write(UC_X86_REG_EDI, self.TEMPLATE)
        self.cpu.reg_write(UC_X86_REG_EBX, signal)
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK+4092)
        self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(self.CODE+bounds[0], self.STOP, count=200)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError('Original routine did not return within instruction limit')
        after = bytes(self.cpu.mem_read(self.OBJECT, 64))
        if any(a != b for index, (a,b) in enumerate(zip(before,after)) if index not in [0xf,0x16,0x17,0x18,0x19]):
            raise ValueError('Original routine changed unexpected object bytes')
        return after[0x19], after[0x16], after[0xf], struct.unpack_from('<H', after, 0x17)[0]

    def maintenance(self, parameters, condition, counter, work_flags, object_flags, state_bit40, balances):
        template = bytearray(171)
        template[0x45], template[0x57] = parameters['limit'], parameters['interval']
        struct.pack_into('<3I', template, 0x8c, *parameters['costs'])
        self.cpu.mem_write(self.FUNDS, struct.pack('<3I', *balances))
        self.cpu.mem_write(self.NEIGHBOR, self.SENTINEL)
        state = self.run((0xc80d,0xc88c), (condition,counter,work_flags,object_flags), template, 0x40 if state_bit40 else 0)
        funds = struct.unpack('<3I', self.cpu.mem_read(self.FUNDS,12))
        if bytes(self.cpu.mem_read(self.NEIGHBOR,12)) != self.SENTINEL:
            raise ValueError('Maintenance changed another owner balance')
        continues = not bool(self.cpu.reg_read(UC_X86_REG_EFLAGS) & 0x40)
        signal = self.cpu.reg_read(UC_X86_REG_EBX) & 255
        # Explicit little-endian output layout consumed by C# checks.
        return struct.pack('<BBBH3IBB', *state, *funds, continues, signal)

    def damage(self, condition, strength, protection, object_flags, reaction):
        template = bytearray(171); template[0x44] = protection
        self.cpu.mem_write(0x1f41, bytes([strength]))
        self.cpu.mem_write(0x1f44, bytes([reaction]))
        state = self.run((0xbe04,0xbe2e), (condition,0xa5,0x5a,object_flags), template)
        return struct.pack('<BBBH', *state)


def generate(game):
    analysis, code = analyze(game)
    files = {p.name.upper():p for p in game.iterdir() if p.is_file()}
    dat = files['ARMY__02.DAT'].read_bytes()
    parameters = []
    for index in [2,3,4]:
        record = dat[2+index*171:2+(index+1)*171]
        parameters.append({'template_index':index,'limit':record[0x45],'interval':record[0x57],
                           'costs':list(struct.unpack_from('<3I',record,0x8c))})
    parameters.append({'template_index':None,'limit':0,'interval':0,'costs':[0xffffffff,0,1]})
    maintenance = {'parameters':parameters, 'conditions':[0,1,79,80,126,127,128,255],
                   'counters':[0,1,2,3,4,254,255], 'work_flags':[0,1,2,0x24],
                   'object_flags':[0,2,0x1a,0x1202], 'state_bit40':[False,True],
                   'balances':[[0,0,0],[0,2,2],[0,1,2],[0,2,1],[0xffffffff]*3]}
    damage = {'condition_range':[0,255], 'strengths':[0,1,3,4,5,79,127,128,254,255],
              'protections':[0,4,127,255], 'object_flags':[0,0x1f,0xe0,0xabcd], 'reactions':[0,1,0x80,0xff]}
    original = OriginalKernel(code)
    digest = hashlib.sha256(); count = 0
    for case in product(*(maintenance[key] for key in ['parameters','conditions','counters','work_flags','object_flags','state_bit40','balances'])):
        digest.update(original.maintenance(*case)); count += 1
    maintenance.update({'case_count':count,'sha256':digest.hexdigest()})
    print(f'Maintenance: {count} original executions', flush=True)
    digest = hashlib.sha256(); count = 0
    for case in product(range(256), *(damage[key] for key in ['strengths','protections','object_flags','reactions'])):
        digest.update(original.damage(*case)); count += 1
    damage.update({'case_count':count,'sha256':digest.hexdigest()})
    print(f'Damage: {count} original executions', flush=True)
    return {'format_version':1, 'source_sha256':{name:analysis['source_sha256'][name] for name in ['NORWIND.EXE','ARMY__02.DAT','ARMY__02.WRK']},
            'maintenance_code_range':[0xc80d,0xc88c], 'damage_code_range':[0xbe04,0xbe2e],
            'output_layout':{'maintenance':'condition:u8,counter:u8,work_flags:u8,object_flags:u16le,balances:3*u32le,continue_action:u8,signal:u8',
                             'damage':'condition:u8,counter:u8,work_flags:u8,object_flags:u16le'},
            'maintenance':maintenance,'damage':damage,
            'restrictions':['Unicorn 2.1.4, original 32-bit instructions; no callbacks skipped.',
                            'Only the shared maintenance routine and per-target damage tail are executed.',
                            'Maintenance uses owner 7 and checks a neighbor balance is unchanged.',
                            'Intervals count invocations; no mapping to real time is established.',
                            'Three balance slots remain unnamed; effect/target selection and full WRK update are outside scope.']}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    report=generate(args.game)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__ == '__main__':
    main()
