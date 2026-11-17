#!/usr/bin/env python3
"""Native receiver selection, entry coordinates, handoff and worker WRK transitions."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle
from forestry_source import source_constants


class DeliveryOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        if self.mode == 'anchor' and address == self.CODE + 0xb02e:
            self.boundary = True; cpu.emu_stop(); return
        ranges = [(0x6885, 0x68d8)]
        ranges += {'search': [(0xc439, 0xc4cd)], 'approach': [(0x8db0, 0x8e0e)],
            'waiting': [(0xc4cd, 0xc50c), (0xc58e, 0xc5d7)],
            'request': [(0x8ffd, 0x900d), (0x8fba, 0x8fbd)],
            'wait': [(0x900d, 0x902f), (0x8fba, 0x8fbd)], 'anchor': [(0xb008, 0xb030)],
            'grant': [(0x123d3, 0x12406), (0xae3a, 0xae57), (0x9e41, 0x9e55)],
            'return': [(0xae3a, 0xae57), (0x9e41, 0x9e55)]}[self.mode]
        allowed = [(self.CODE+a, self.CODE+b) for a,b in ranges]
        if self.mode == 'grant': allowed += [(self.WRK+0x7f5,self.WRK+0x80e),(self.WRK+0x8a7,self.WRK+0x8aa),(self.WRK+0x851,self.WRK+0x85e),(self.WRK+0x8ce,self.WRK+0x8e9),(self.WRK+0x8fe,self.WRK+0x901),(self.WRK+0x91a,self.WRK+0x938)]
        if self.mode == 'return': allowed += [(self.WRK+0x9b2,self.WRK+0x9d2),(self.WRK+0x7f5,self.WRK+0x7f9),(self.WRK+0x815,self.WRK+0x833),(self.WRK+0x97d,self.WRK+0x980)]
        if not any(a <= address < b for a,b in allowed): raise ValueError(f'Delivery escape {self.mode}: {address:#x}')

    def start(self, address):
        super().start(address)
        if self.mode != 'anchor' and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError('Native delivery operation did not return')

    def setup(self, raw):
        self.cpu.mem_write(self.POOL+1, bytes(raw)); self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+1)
        self.cpu.reg_write(UC_X86_REG_EDI,self.POOL+1)

    def read(self): return bytes(self.cpu.mem_read(self.POOL+1,54))


def seed():
    raw=bytearray((i*7+5)&255 for i in range(54));raw[0]=2;raw[0x0e]=10;raw[0x31]=0
    return raw


def generate(game):
    report,code=analyze(game);o=DeliveryOracle(code,report['fs_assignments']);o.prepare(8,8)
    dat=(game/'ARMY__02.DAT').read_bytes();o.cpu.mem_write(o.DAT,dat);o.cpu.mem_write(o.WRK,(game/'ARMY__02.WRK').read_bytes())
    o.cpu.mem_write(0x6ca+2*0x56+4,struct.pack('<I',o.DAT+2));o.cpu.mem_write(0x6ca+2*0x56+9,struct.pack('<I',o.WRK))
    o.cpu.mem_write(0x297e,bytes(source_constants(game)['random_values']))
    result={'format_version':1,'source_sha256':{k:report['source_sha256'][k]for k in ['NORWIND.EXE','ARMY__02.DAT','ARMY__02.WRK']},'results':{}}
    def save(name,digest,count):
        result['results'][name]={'case_count':count,'sha256':digest.hexdigest()};print(name,count,flush=True)
    spec={'origins':[[0,0],[128,128],[4294967295,2147483648]],'positions':[[[64,64],[64,64],[160,0]],[[192,160],[160,192],[320,320]],[[2147483648,0],[0,2147483648],[4294967295,4294967295]]],
          'thresholds':[0,10,127,128,255],'flags':[512,520,768,4608]}
    result['search']=spec;digest=hashlib.sha256();count=0;o.mode='search'
    for origin,positions,condition,threshold,flags in product(spec['origins'],spec['positions'],range(256),spec['thresholds'],spec['flags']):
        raw=seed();raw[3]=3;struct.pack_into('<II',raw,4,*origin);o.setup(raw)
        o.cpu.mem_write(0x97a+3*0xf5+9,struct.pack('<I',0x100+3*37));o.cpu.mem_write(0x97a+3*0xf5+14,struct.pack('<I',0x100))
        for i,(x,y) in enumerate(positions):
            target=bytearray(37);target[0]=2;target[0x0e]=i;target[0x19]=condition if i==0 else 70
            struct.pack_into('<II',target,4,x,y);struct.pack_into('<I',target,0x10,37);o.cpu.mem_write(o.POOL+0x100+i*37,bytes(target))
            template=o.DAT+2+i*171;o.cpu.mem_write(template+0x40,struct.pack('<I',flags if i==0 else 512));o.cpu.mem_write(template+0x46,bytes([threshold if i==0 else 10]))
        o.cpu.reg_write(UC_X86_REG_EAX,512);o.cpu.reg_write(UC_X86_REG_EBX,0xf00);o.start(o.CODE+0xc439)
        found=o.cpu.reg_read(UC_X86_REG_EDI);digest.update(struct.pack('<I',found-o.POOL if found else 0));count+=1
    save('search',digest,count)
    spec={'widths':[1,2,255],'flags':[0,8],'offsets':[[176,16,160,0],[0,0,0,0],[128,127,255,1]],'positions':[[0,0],[16,16],[128,240],[4294967295,4294967295]]}
    result['approach']=spec;digest=hashlib.sha256();count=0;o.mode='approach'
    for width,flags,offsets,position in product(spec['widths'],spec['flags'],spec['offsets'],spec['positions']):
        raw=seed();o.setup(raw);target=seed();target[0x0e]=4;struct.pack_into('<II',target,4,*position);o.cpu.mem_write(o.POOL+0x100,bytes(target))
        o.cpu.mem_write(o.DAT+2+10*171+3,bytes([width]));t=o.DAT+2+4*171;o.cpu.mem_write(t+0x40,struct.pack('<I',flags));o.cpu.mem_write(t+0x53,bytes(offsets))
        o.cpu.mem_write(0x18e6,struct.pack('<II',4096,4096));o.cpu.reg_write(UC_X86_REG_EDI,o.POOL+0x100);o.start(o.CODE+0x8db0)
        al=o.cpu.reg_read(UC_X86_REG_EAX)&255;digest.update(struct.pack('<IIBB',o.cpu.reg_read(UC_X86_REG_ECX),o.cpu.reg_read(UC_X86_REG_EDX),al!=255,al==1));count+=1
    save('approach',digest,count)
    digest=hashlib.sha256();count=0;o.mode='anchor'
    for x,y,fw,fh in product([0,1,127,254],repeat=4):
        raw=seed();raw[0x32]=x;raw[0x33]=y;o.setup(raw);o.cpu.mem_write(o.DAT+3,bytes([fw,fh]));o.cpu.reg_write(UC_X86_REG_EDI,o.DAT)
        o.boundary=False;o.start(o.CODE+0xb008)
        if not o.boundary:raise ValueError('Anchor boundary missing')
        sp=o.cpu.reg_read(UC_X86_REG_ESP);yy,xx=struct.unpack('<II',bytes(o.cpu.mem_read(sp,8)));digest.update(struct.pack('<II',xx,yy));count+=1
    save('anchor',digest,count)
    digest=hashlib.sha256();count=0;o.mode='request'
    for flags1,flags2,packed in product([0,1,64,128,65535],[0,1,64,128,65535],[0,3,255]):
        raw=seed();struct.pack_into('<H',raw,0x17,flags1);raw[0x30]=packed;o.setup(raw);target=bytearray(37);struct.pack_into('<H',target,0x17,flags2);o.cpu.mem_write(o.POOL+0x100,bytes(target))
        o.cpu.reg_write(UC_X86_REG_EDI,o.POOL+0x100);o.start(o.CODE+0x8ffd);digest.update(o.read()+bytes(o.cpu.mem_read(o.POOL+0x100,37)));count+=1
    save('request',digest,count)
    digest=hashlib.sha256();count=0;o.mode='wait'
    for packed,flags in product(range(256),[0,1,64,128,65535]):
        raw=seed();raw[0x30]=packed;struct.pack_into('<H',raw,0x17,flags);o.setup(raw);o.cpu.reg_write(UC_X86_REG_EBX,255);o.cpu.reg_write(UC_X86_REG_ECX,(packed&15)|((packed>>4)<<8));o.start(o.CODE+0x900d)
        digest.update(o.read()+bytes([o.cpu.reg_read(UC_X86_REG_EBX)&255]));count+=1
    save('wait',digest,count)
    # Restore real templates after synthetic lookup profiles.
    o.cpu.mem_write(o.DAT,dat)
    result['random_indices']=[0,1,127,255];result['headings']=[0,7,63,255];result['grant_flags']=[0,131072,131840,4294967295]
    digest=hashlib.sha256();count=0;o.mode='grant'
    for condition,flags,heading,index in product(range(256),result['grant_flags'],result['headings'],result['random_indices']):
        raw=seed();raw[0x19]=condition;raw[0x25]=heading;o.setup(raw);o.cpu.mem_write(o.DAT+2+10*171+0x40,struct.pack('<I',flags));o.cpu.mem_write(0x19eb,bytes([index]));o.cpu.reg_write(UC_X86_REG_EAX,3)
        o.start(o.CODE+0x123d3);after=o.read();digest.update(after+bytes(o.cpu.mem_read(0x19eb,1))+bytes([after[0x0e]==11]));count+=1
    save('grant',digest,count)
    digest=hashlib.sha256();count=0;o.mode='return';o.cpu.mem_write(o.DAT,dat)
    for heading,index in product(range(256),result['random_indices']):
        raw=seed();raw[0x0e]=11;raw[0x25]=heading;o.setup(raw);o.cpu.mem_write(0x19eb,bytes([index]));o.start(o.WRK+0x9b2)
        digest.update(o.read()+bytes(o.cpu.mem_read(0x19eb,1)));count+=1
    save('return',digest,count)
    digest=hashlib.sha256();count=0;o.mode='waiting';o.cpu.mem_write(o.DAT,dat)
    for x,y,layer,reference,flags in product([0,16,128,240],[0,16,128,240],[0,1,2,129],[0,0x100],[0,64,65535]):
        o.prepare(8,8);raw=seed();raw[0x0e]=4;raw[0x0d]=layer;struct.pack_into('<II',raw,4,x,y);o.setup(raw)
        visitor=seed();visitor[0x0e]=11;struct.pack_into('<H',visitor,0x17,flags);o.cpu.mem_write(o.POOL+0x100,bytes(visitor))
        grid=[0]*192;grid[(layer&3)*64:((layer&3)+1)*64]=[reference]*64
        o.cpu.mem_write(o.GRID,struct.pack('<192I',*grid));o.start(o.CODE+0xc4cd)
        found=o.cpu.reg_read(UC_X86_REG_EDI);digest.update(struct.pack('<I',found-o.POOL if found else 0));count+=1
    save('waiting',digest,count)
    result['templates_sha256']={str(i):hashlib.sha256(dat[2+i*171:2+(i+1)*171]).hexdigest()for i in[4,10,11]}
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',type=Path,required=True);p.add_argument('--fixture',type=Path,required=True);a=p.parse_args()
    a.fixture.write_text(json.dumps(generate(a.game),indent=2)+'\n',encoding='utf-8')
