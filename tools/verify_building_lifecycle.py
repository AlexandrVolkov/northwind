#!/usr/bin/env python3
"""Run original command/transition instructions and fresh castle allocation.

Fresh allocation includes the real WRK AH=0 callback. Map placement, upfront
construction payment, selection validation and the complete UI are excluded.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from verify_building_kernel import OriginalKernel
from analyze_executable import analyze


class LifecycleOracle(OriginalKernel):
    WRK, UI = 0x800000, 0x700000

    def __init__(self, code, dat, wrk):
        super().__init__(code)
        self.allowed = []
        self.cpu.mem_map(self.TEMPLATE+4096, 4096)
        self.cpu.mem_map(self.WRK, (len(wrk)+4095)//4096*4096)
        self.cpu.mem_map(self.UI, 4096)
        self.cpu.mem_write(self.TEMPLATE, dat)
        self.cpu.mem_write(self.WRK, wrk)

    def guard(self, cpu, address, size, user_data):
        if not any(start <= address < end for start,end in self.allowed):
            raise ValueError(f'Unexpected lifecycle control-flow escape: {address:#x}')

    def execute(self, start, end=None):
        from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS
        self.cpu.mem_write(self.STACK+0xf00, struct.pack('<4I',self.STOP,0,0,0))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK+0xf00)
        self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        until = self.CODE+end if end is not None else self.STOP
        self.cpu.emu_start(self.CODE+start, until, count=1000)
        if self.cpu.reg_read(UC_X86_REG_EIP) != until:
            raise ValueError('Original lifecycle block did not reach its boundary')

    def queue(self, state, slot):
        from unicorn.x86_const import UC_X86_REG_ESI
        self.allowed = [(self.CODE+0x61c7,self.CODE+0x61d4),(self.CODE+0x623d,self.CODE+0x6250)]
        before = bytearray(b'\x5a'*49); before[0x14] = state
        self.cpu.mem_write(self.OBJECT,bytes(before))
        self.cpu.mem_write(self.UI+2,bytes([slot]))
        self.cpu.mem_write(0x1969,struct.pack('<I',self.OBJECT))
        self.cpu.reg_write(UC_X86_REG_ESI,self.UI)
        self.execute(0x61c7,0x61d4)
        self.execute(0x623d)
        after = bytes(self.cpu.mem_read(self.OBJECT,49))
        if any(a != b for i,(a,b) in enumerate(zip(before,after)) if i != 0x14):
            raise ValueError('Queuing changed unrelated object fields')
        return after[0x14:0x15]

    def complete(self,state,work_flags,auxiliary):
        from unicorn.x86_const import UC_X86_REG_ESI
        self.allowed = [(self.CODE+0xc8c9,self.CODE+0xc8e0)]
        before = bytearray(b'\x5a'*49)
        before[0x14],before[0xf],before[0x15] = state,work_flags,auxiliary
        self.cpu.mem_write(self.OBJECT,bytes(before))
        self.cpu.reg_write(UC_X86_REG_ESI,self.OBJECT)
        self.execute(0xc8c9,0xc8e0)
        after=bytes(self.cpu.mem_read(self.OBJECT,49))
        if any(a != b for i,(a,b) in enumerate(zip(before,after)) if i not in [0xf,0x14,0x15]):
            raise ValueError('Transition changed unrelated object fields')
        return bytes([after[0x14],after[0x15],after[0xf]])

    def create(self,army,owner,layer,coordinates,frame,condition,construction):
        from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_ESI
        self.allowed = [(self.CODE+0xa758,self.CODE+0xa879),(self.WRK+0xfe,self.WRK+0x126),
                        (self.CODE+0xb16a,self.CODE+0xb176)]
        # Actual DAT and WRK bases; the native routine selects template 2 itself.
        self.cpu.mem_write(0x6ca+army*0x56+4,struct.pack('<I',self.TEMPLATE+2))
        self.cpu.mem_write(0x6ca+army*0x56+9,struct.pack('<I',self.WRK))
        record = self.TEMPLATE+2+2*171
        self.cpu.mem_write(record+0x45,bytes([condition]))
        self.cpu.mem_write(record+0x48,struct.pack('<H',frame))
        self.cpu.mem_write(0x6aa,struct.pack('<I',self.OBJECT))
        account = 0x97a+owner*0xf5
        for offset,value in [(5,self.OBJECT),(9,0),(0xe,0)]:
            self.cpu.mem_write(account+offset,struct.pack('<I',value))
        self.cpu.mem_write(self.OBJECT,b'\xa5'*64)
        self.cpu.reg_write(UC_X86_REG_EAX,army|(owner<<8))
        self.cpu.reg_write(UC_X86_REG_EBX,2|(layer<<8))
        self.cpu.reg_write(UC_X86_REG_ECX,coordinates[0])
        self.cpu.reg_write(UC_X86_REG_EDX,coordinates[1])
        self.cpu.reg_write(UC_X86_REG_ESI,0)
        self.execute(0xa758)
        if struct.unpack('<I',self.cpu.mem_read(account+9,4))[0] != 49:
            raise ValueError('Fresh allocation did not extend the chain by 49 bytes')
        if bytes(self.cpu.mem_read(self.OBJECT+49,15)) != b'\xa5'*15:
            raise ValueError('Fresh allocation changed bytes beyond the record')
        if construction:
            self.execute(0xb16a,0xb176)
        return bytes(self.cpu.mem_read(self.OBJECT,49))


def generate(game):
    analysis,code=analyze(game)
    files={p.name.upper():p for p in game.iterdir() if p.is_file()}
    oracle=LifecycleOracle(code,files['ARMY__02.DAT'].read_bytes(),files['ARMY__02.WRK'].read_bytes())
    queue={'state_range':[0,255],'slots':list(range(6))}
    complete={'state_range':[0,255],'work_flags':[0,1,2,0x24,0xff],'auxiliary':[0,128,255]}
    create={'army_slots':[0,2,4],'owners':[0,3,7],'layers':[0,1,3],
            'coordinates':[[0,0],[608,720],[0xffffffff,0x80000000]],'frames':[876,65535],
            'conditions':[0,127,255],'construction':[False,True]}
    for spec,cases,callback in [
        (queue,product(range(256),queue['slots']),oracle.queue),
        (complete,product(range(256),complete['work_flags'],complete['auxiliary']),oracle.complete),
        (create,product(*(create[key] for key in ['army_slots','owners','layers','coordinates','frames','conditions','construction'])),oracle.create)]:
        digest=hashlib.sha256(); count=0
        for case in cases:
            digest.update(callback(*case));count+=1
        spec.update({'case_count':count,'sha256':digest.hexdigest()})
        print(f'Original lifecycle: {count} cases',flush=True)
    return {'format_version':1,'source_sha256':{name:analysis['source_sha256'][name] for name in ['NORWIND.EXE','ARMY__02.DAT','ARMY__02.WRK']},
            'queue':queue,'complete':complete,'create':create,
            'ranges':{'queue':[[0x61c7,0x61d4],[0x623d,0x6250]],'complete':[[0xc8c9,0xc8e0]],
                      'create':[[0xa758,0xa879]],'wrk_callback':[[0xfe,0x126]],'construction':[[0xb16a,0xb176]]},
            'output_layout':{'queue':'state:u8','complete':'state:u8,auxiliary:u8,work_flags:u8','create':'49 raw object bytes'},
            'restrictions':['Unicorn 2.1.4; no callbacks skipped, including the actual castle WRK initialization.',
                            'Fresh allocation into an empty owner chain only; existing gaps and allocation failure excluded.',
                            'Single-object direct-mode command application, not group-selection or full UI validation.',
                            'Construction marks fields only; upfront cost and occupancy update excluded.',
                            'Factory checked only for the 49-byte castle shape and template index 2.']}


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
