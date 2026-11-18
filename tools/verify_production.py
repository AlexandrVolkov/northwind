#!/usr/bin/env python3
"""Execute bounded native production instructions; export golden hashes and castle/barracks WRK tables."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn import UcError, UC_ERR_EXCEPTION
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class ProductionOracle(OccupancyOracle):
    def __init__(self, code, assignments):
        super().__init__(code, assignments)
        self.cpu.mem_write(0x6ce + 2 * 0x56, struct.pack('<I', self.DAT))

    def guard(self, cpu, address, size, user_data):
        bounds = {'availability': [(0x62fe,0x6304)], 'enqueue':[(0x6330,0x6342)],
                  'start':[(0xa90f,0xa920)], 'tick':[(0xa934,0xa962),(0xaa5a,0xaa65),(0x68a0,0x68bd)],
                  'advance':[(0xaa36,0xaa65)], 'refund':[(0xaa76,0xaab1)],
                  'exit':[(0xa962,0xa9a4),(0x6885,0x68a0)]}
        if self.mode=='availability' and address == self.CODE+0x6313:
            self.available=True; cpu.emu_stop(); return
        if self.mode=='tick' and address == self.CODE+0xa962:
            self.ready=True; cpu.emu_stop(); return
        if not any(self.CODE+a<=address<self.CODE+b for a,b in bounds[self.mode]):
            raise ValueError(f'Unexpected production {self.mode} escape {address:#x}')

    def record(self):
        raw=bytearray([0xa5]*49);raw[0]=2;raw[3]=0;raw[0x0e]=2
        raw[0x14]=0xc9;raw[0x15]=0;raw[0x22:0x27]=b'\xff'*5
        return raw

    def run(self, mode, address, raw, end=None):
        self.mode=mode; self.cpu.mem_write(self.POOL, bytes(raw));self.cpu.reg_write(UC_X86_REG_ESI,self.POOL)
        if end is None: self.start(self.CODE+address)
        else:self.cpu.emu_start(self.CODE+address,self.CODE+end,count=1000)
        expected=self.CODE+end if end is not None else self.STOP
        if mode=='tick' and self.ready:expected=self.CODE+0xa962
        if self.cpu.reg_read(UC_X86_REG_EIP)!=expected:raise ValueError(f'{mode} did not reach expected end')
        return bytes(self.cpu.mem_read(self.POOL,49))

    def enqueue(self, case):
        raw=self.record();raw[0x22:0x27]=bytes(case[:5]);self.available=False;self.mode='availability'
        self.cpu.mem_write(self.POOL,bytes(raw));self.cpu.reg_write(UC_X86_REG_EBX,self.POOL)
        self.cpu.emu_start(self.CODE+0x62fe,self.CODE+0x6304,count=100)
        if self.cpu.reg_read(UC_X86_REG_EIP)!=self.CODE+(0x6313 if self.available else 0x6304):raise ValueError('Queue availability boundary')
        if self.available:
            self.cpu.mem_write(self.POOL+0x1000,b'\0\0'+bytes([case[5]]))
            self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+0x1000)
            self.cpu.mem_write(self.STACK+0xf00,struct.pack('<I',self.STOP));self.cpu.reg_write(UC_X86_REG_ESP,self.STACK+0xf00)
            self.mode='enqueue';self.cpu.emu_start(self.CODE+0x6330,self.STOP,count=100)
            if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP:raise ValueError('Queue append did not return')
        return bytes([self.available])+bytes(self.cpu.mem_read(self.POOL,49))

    def start_paid(self, phase):
        raw=self.record();raw[0x15]=phase
        return self.run('start',0xa90f,raw)

    def tick(self, case):
        interval,shift,counter,progress,limit=case;raw=self.record();raw[0x22]=0;raw[0x28]=counter;raw[0x27]=progress;raw[0x15]=1
        self.cpu.mem_write(self.PARTICIPANTS+0x1a26,bytes([shift]));self.cpu.mem_write(self.DAT+0x45,bytes([limit]))
        self.cpu.reg_write(UC_X86_REG_EDX,self.PARTICIPANTS);self.cpu.reg_write(UC_X86_REG_EBX,interval)
        self.ready=False
        result=self.run('tick',0xa934,raw)
        return bytes([self.ready])+result

    def advance(self, queue):
        raw=self.record();raw[0x22:0x27]=bytes(queue);raw[0x15]=1
        return self.run('advance',0xaa36,raw)

    def refund(self, case):
        costs,balances,progress,limit=case;raw=self.record();raw[0x27]=progress
        self.cpu.mem_write(self.DAT+0x7c,struct.pack('<4I',*costs));self.cpu.mem_write(self.DAT+0x45,bytes([limit]))
        self.cpu.mem_write(self.PARTICIPANTS,b'\xa5'*0xf5);self.cpu.mem_write(self.PARTICIPANTS+0x18,struct.pack('<4I',*balances))
        self.cpu.reg_write(UC_X86_REG_EDI,self.DAT);self.cpu.reg_write(UC_X86_REG_ECX,self.PARTICIPANTS)
        try:self.run('refund',0xaa76,raw,0xaab1)
        except UcError as e:
            if e.errno!=UC_ERR_EXCEPTION:raise
            return b'\x01'
        owner=bytes(self.cpu.mem_read(self.PARTICIPANTS,0xf5))
        if owner[:0x18]!=b'\xa5'*0x18 or owner[0x28:]!=b'\xa5'*(0xf5-0x28):raise ValueError('Refund changed unrelated owner fields')
        return b'\0'+owner[0x18:0x28]

    def exit(self, case):
        x,y,width,height,offsets,layer=case;raw=self.record();struct.pack_into('<II',raw,4,x,y)
        unit=bytearray(171);unit[3]=width;unit[4]=height;unit[0x77]=layer
        building=bytearray(171);building[0x53:0x57]=bytes(offsets)
        self.cpu.mem_write(self.DAT,bytes(unit));self.cpu.mem_write(self.DAT+2*171,bytes(building));self.cpu.reg_write(UC_X86_REG_EDI,self.DAT)
        self.run('exit',0xa962,raw,0xa9a4)
        return struct.pack('<IIB',self.cpu.reg_read(UC_X86_REG_ECX),self.cpu.reg_read(UC_X86_REG_EDX),self.cpu.reg_read(UC_X86_REG_EBX)&255)


def generate(game):
    report,code=analyze(game);oracle=ProductionOracle(code,report['fs_assignments']);result=dict(format_version=1,source_sha256=report['source_sha256']['NORWIND.EXE'])
    queues=[[255]*5,[10,255,255,255,255],[10,0,10,0,255],[10,0,10,0,10],[0,255,10,255,255]]
    cases={
      'enqueue':[(q+[t]) for q in queues for t in [0,10,254]],
      'start': [0,1,254,255],
      'tick':list(product([0,1,30,40,255],[0,1,7,8,31,32,255],[0,1,127,254,255],[0,4,5,9,10,254,255],[0,5,10,255])),
      'advance':queues,
      'refund':[(costs,balances,progress,limit) for costs in [[0,0,0,0],[5,0,40,1],[10,0,40,1],[300,65537,65535,0xffffffff],[0xffffffff,0x800000ff,0x12345678,2]]
        for balances in [[0,0,0,0],[0xffffffff]*4] for progress in [0,1,5,35,70,127,255] for limit in [1,5,70,255] if max((c & 255)*progress//limit for c in costs[:3]) <= 255],
      'exit':[(x,y,w,h,offsets,layer) for x,y in [(608,720),(0,0),(0xffffffff,0x80000000)] for w,h in [(1,1),(1,2),(2,1),(4,3)]
        for offsets in [[48,64,32,112],[128,255,127,1]] for layer in [1,130,255]]}
    execute={'enqueue':oracle.enqueue,'start':oracle.start_paid,'tick':oracle.tick,'advance':oracle.advance,'refund':oracle.refund,'exit':oracle.exit}
    for name,inputs in cases.items():
        digest=hashlib.sha256()
        for c in inputs:digest.update(execute[name](c))
        result[name]=dict(cases=inputs,case_count=len(inputs),sha256=digest.hexdigest());print(name,len(inputs),digest.hexdigest(),flush=True)
    wrk=(game/'ARMY__02.WRK').read_bytes();tables={}
    for name,base in [('castle',0x221),('barracks',0x336)]:
        entries=[];offset=base+7
        while wrk[offset]!=255:
            entry=wrk[offset:offset+4];entries.append(dict(template=entry[0],icon_frame=int.from_bytes(entry[1:3],'little'),interval=entry[3],raw_hex=entry.hex()));offset+=4
        tables[name]=dict(table_offset=base,raw_header_hex=wrk[base:base+7].hex(),entries=entries)
    native={name:{e['template']:e['interval'] for e in t['entries']} for name,t in tables.items()}
    data=dict(format_version=1,source_file='ARMY__02.WRK',source_sha256=hashlib.sha256(wrk).hexdigest(),tables=tables,
              recruitment_intervals={'10':native['castle'][10],'0':native['barracks'][0]},
              limitations='Militia retains the experimental castle menu, but its interval belongs to barracks. Seven-byte headers are not production rows.')
    if data['recruitment_intervals']!={'10':30,'0':40}:raise ValueError('Unexpected supported production intervals')
    return result,data


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',type=Path,required=True);p.add_argument('--fixture',type=Path,required=True);p.add_argument('--data',type=Path,required=True);args=p.parse_args()
    result,data=generate(args.game)
    args.fixture.write_text(json.dumps(result,separators=(',',':'))+'\n');args.data.write_text(json.dumps(data,indent=2)+'\n')
