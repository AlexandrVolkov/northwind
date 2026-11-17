#!/usr/bin/env python3
"""Execute complete 8F70 approach with real lumbermill WRK validation, plus turn animation."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_worker_delivery import DeliveryOracle
from verify_worker_animation import WorkerAnimationOracle, record, template
from forestry_source import source_constants


class ApproachOracle(DeliveryOracle):
    def guard(self,cpu,address,size,user_data):
        if address==self.CODE+0x8ca1:self.move=True
        if address==self.CODE+0x8c98:self.stop=True
        ranges=[(0x8f70,0x902f),(0x8a7f,0x8a9f),(0x8d89,0x8db0),(0x8db0,0x8e0e),(0x8bea,0x8c1e),
                (0x8c98,0x8cbb),(0x960b,0x9688),(0x9b86,0x9bf9),(0xd182,0xd19c),(0x6885,0x68d8)]
        if self.CODE+0x8cb1==address:raise ValueError('Unexpected path cancellation call')
        if not any(self.CODE+a<=address<self.CODE+b for a,b in ranges) and not self.WRK+0x354<=address<self.WRK+0x383:
            raise ValueError(f'Approach escape: {address:#x}')


class TurnOracle(WorkerAnimationOracle):
    def guard(self,cpu,address,size,user_data):
        if self.CODE+0x9354<=address<self.CODE+0x937f or self.CODE+0x9653<=address<self.CODE+0x9688:return
        super().guard(cpu,address,size,user_data)


def approach_record(packed,state,heading,profile):
    raw=bytearray((i*11+9)&255 for i in range(54));raw[0]=2;raw[0x0e]=11;raw[0x19]=5
    struct.pack_into('<II',raw,4,*profile[:2]);struct.pack_into('<I',raw,0x1c,0x100)
    raw[0x30]=packed;raw[0x2d]=state;raw[0x25]=heading;raw[0x17]=70 if profile[4]&8 else 6
    return raw


def generate(game, trace_path=None):
    report,code=analyze(game);o=ApproachOracle(code,report['fs_assignments']);o.mode='approach'
    dat=(game/'ARMY__02.DAT').read_bytes();o.cpu.mem_write(o.DAT,dat);o.cpu.mem_write(o.WRK,(game/'ARMY__02.WRK').read_bytes())
    o.cpu.mem_write(0x6aa,struct.pack('<I',o.POOL));o.cpu.mem_write(0x6ca+2*0x56+4,struct.pack('<I',o.DAT+2));o.cpu.mem_write(0x6ca+2*0x56+9,struct.pack('<I',o.WRK))
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    headings=objects[1]['payload'][0x19d7:0x19e0];o.cpu.mem_write(0x19d7,headings)
    result={'format_version':1,'source_sha256':{k:report['source_sha256'][k]for k in ['NORWIND.EXE','ARMY__02.DAT','ARMY__02.WRK']},
        'states':[0,1,2,3,16,32,34,48,51,18,255], 'visitor_flags':[131840,135936,131328,4294967295], 'headings':[0,63],
        'profiles':[[176,240,256,224,8,1,1,4,2,1456,11],[144,240,256,224,8,1,1,4,2,1456,11],
                    [16,16,512,512,8,1,1,4,2,1456,11],[16,16,0,0,8,1,1,4,2,1456,11],
                    [145,224,256,224,0,1,1,4,2,1456,11],[144,224,256,224,0,1,1,4,2,1456,11],
                    [4294967295,2147483648,256,224,0,255,16,16,255,65535,255]],'results':{}}
    def save(name,digest,count):result['results'][name]={'case_count':count,'sha256':digest.hexdigest()};print(name,count,flush=True)
    trace=trace_path.open('wb') if trace_path else None
    digest=hashlib.sha256();count=0
    for packed,state,flags,heading,p in product(range(256),result['states'],result['visitor_flags'],result['headings'],result['profiles']):
        raw=approach_record(packed,state,heading,p);o.setup(raw)
        target=bytearray((i*3+7)&255 for i in range(37));target[0]=2;target[0x0e]=4;target[0x17]=0
        struct.pack_into('<II',target,4,*p[2:4]);o.cpu.mem_write(o.POOL+0x100,bytes(target))
        own=o.DAT+2+11*171;dest=o.DAT+2+4*171;o.cpu.mem_write(own+3,bytes(p[5:7]));o.cpu.mem_write(dest+3,bytes(p[7:9]));o.cpu.mem_write(dest+0x40,struct.pack('<I',p[4]|512))
        o.cpu.mem_write(dest+0x53,bytes([176,16,160,0]));o.cpu.mem_write(own+0x40,struct.pack('<I',flags));o.cpu.mem_write(own+0x48,struct.pack('<HB',p[9],p[10]))
        o.cpu.mem_write(0x1c49,struct.pack('<I',own));o.cpu.mem_write(0x18e6,struct.pack('<II',4096,4096))
        o.cpu.reg_write(UC_X86_REG_EBX,0);o.move=o.stop=False;o.start(o.CODE+0x8f70)
        zf=bool(o.cpu.reg_read(UC_X86_REG_EFLAGS)&64);status=o.cpu.reg_read(UC_X86_REG_EBX)&255 if zf else 3
        if status not in[0,1,2,3]:raise ValueError('Unknown native approach result')
        encoded=o.read()+bytes(o.cpu.mem_read(o.POOL+0x100,37))+bytes([status,o.move,o.stop])
        digest.update(encoded)
        if trace:trace.write(encoded)
        count+=1
    if trace:trace.close()
    save('approach',digest,count)
    # Explicit missing reference: native 8A7F rejects before calling WRK or reading target bytes.
    raw=approach_record(243,0,0,result['profiles'][0]);struct.pack_into('<I',raw,0x1c,0);o.setup(raw);o.move=o.stop=False;o.start(o.CODE+0x8f70)
    result['missing_record_hex']=o.read().hex()
    turn=TurnOracle(code,report['fs_assignments'],source_constants(game));turn.cpu.mem_write(0x19d7,headings)
    result['turn']={'states':[16,17,1,49], 'timers':list(range(256)), 'headings':list(range(64))+[255], 'flags':[0,128], 'configurations':[[1456,11,0,0,0,0],[65535,255,0,0,0,0]]}
    spec=result['turn'];digest=hashlib.sha256();count=0
    for config,state,timer,heading,flags in product(spec['configurations'],spec['states'],spec['timers'],spec['headings'],spec['flags']):
        turn.worker(bytes(record(state,timer,17,heading,flags)),bytes(template(config)));event=turn.animation()
        if event[0]:raise ValueError('Turn unexpectedly emitted a strike effect')
        digest.update(turn.record());count+=1
    save('turn',digest,count)
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',type=Path,required=True);p.add_argument('--fixture',type=Path,required=True);p.add_argument('--trace',type=Path);a=p.parse_args()
    a.fixture.write_text(json.dumps(generate(a.game,a.trace),indent=2)+'\n',encoding='utf-8')
