#!/usr/bin/env python3
"""Execute native contour planning and hash routes and the ordered candidate probe trace."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_movement_admission import AdmissionOracle

DELTAS=[(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1)]

class SearchOracle(AdmissionOracle):
    def guard(self,cpu,address,size,user_data):
        if address == self.CODE+0x7d4c:
            x,y=cpu.mem_read(0x274e,2);d=cpu.reg_read(UC_X86_REG_EAX)&255
            dx,dy=DELTAS[d];self.trace.extend([(x+dx)&255,(y+dy)&255,d])
        if not any(self.CODE+a <= address < self.CODE+b for a,b in [(0x73f5,0x7a03),(0x7d4c,0x7fa7),(0x6877,0x68a0)]):
            raise ValueError(f'Unexpected planner execution: {address:#x}')
    def search(self,c):
        w,h=c['size'];fw,fh=c['footprint'];x,y=c['start'];gx,gy=c['target']
        self.prepare(w,h);self.cpu.mem_write(0x1f00,bytes(0x900))
        dat=bytearray(171);dat[3:5]=bytes([fw,fh]);dat[0x58]=8;dat[0x59]=0
        self.cpu.mem_write(self.DAT,bytes(dat));self.cpu.mem_write(0x6ce+2*0x56,struct.pack('<I',self.DAT))
        self.cpu.mem_write(0x98c,bytes(4))
        raw=bytearray(54);raw[0]=2;raw[0x0d]=1
        struct.pack_into('<II',raw,4,(x+fw//2)*32+16,(y+fh//2)*32+16)
        self.cpu.mem_write(self.POOL+1,bytes(raw));self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+1)
        cells=bytes.fromhex(c['blocked']);self.cpu.mem_write(self.MAP,b''.join(struct.pack('<I',v) for v in cells))
        self.cpu.mem_write(self.SEG,bytes(8)+bytes([1])+bytes(7))
        self.cpu.mem_write(0x18ee,struct.pack('<I',self.MAP));self.cpu.mem_write(0x18fe,struct.pack('<I',self.SEG));self.cpu.mem_write(0x525,bytes([gx,gy]))
        self.trace=bytearray();self.cpu.mem_write(self.STACK+0xf00,struct.pack('<I',self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP,self.STACK+0xf00);self.cpu.reg_write(UC_X86_REG_EFLAGS,2)
        self.cpu.emu_start(self.CODE+0x73f5,self.STOP,count=20000000)
        if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP:raise ValueError(f'Native planner did not return: {c["name"]}')
        pointer=struct.unpack('<I',self.cpu.mem_read(0x2730,4))[0]
        end=bytes(self.cpu.mem_read(0x274e,2));route=bytearray()
        if pointer!=0xffffffff:
            for i in range(2100):
                v=self.cpu.mem_read(pointer+i,1)[0]
                if v&8:break
                route.append(v)
            else:raise ValueError('Unterminated native path')
        packed=bytes([pointer!=0xffffffff])+struct.pack('<H',len(route))+bytes(route)+end+struct.pack('<I',len(self.trace)//3)+self.trace
        return packed,bytes(route),pointer!=0xffffffff


def make_cases():
    cases=[]
    def add(name,w,h,fw,fh,start,target,blocked):
        cells=bytearray(w*h)
        for x,y in blocked:
            if 0<=x<w and 0<=y<h:cells[y*w+x]=1
        # Clear current footprint only. Goal may intentionally be blocked.
        for y in range(start[1],start[1]+fh):
            for x in range(start[0],start[0]+fw):cells[y*w+x]=0
        cases.append(dict(name=name,size=[w,h],footprint=[fw,fh],start=list(start),target=list(target),blocked=cells.hex()))
    # Every endpoint on a small clear map, including coincidence and every octant.
    for gy in range(8):
        for gx in range(8):add(f'clear-{gx}-{gy}',8,8,1,1,(3,3),(gx,gy),[])
    for fw,fh in [(1,1),(2,2),(1,2),(3,2)]:
        for gap in [-1,0,3,7,12,15]:
            blocked=[(8,y) for y in range(16) if y!=gap]
            add(f'wall-{fw}-{fh}-{gap}',16,16,fw,fh,(2,6),(12,6),blocked)
        add(f'u-{fw}-{fh}',32,32,fw,fh,(14,14),(25,14),[(x,y) for y in range(7,23) for x in range(7,23) if x==22 or y in [7,22]])
        add(f'box-{fw}-{fh}',32,32,fw,fh,(14,14),(25,14),[(x,y) for y in range(7,23) for x in range(7,23) if x in [7,22] or y in [7,22]])
    for start,target in [((10,10),(110,50)),((60,60),(0,127)),((127,127),(0,0)),((0,0),(127,127)),((16,16),(17,17)),((17,17),(0,0))]:
        add(f'window-{start}-{target}',128,128,1,1,start,target,[])
    # Reproducible random maps; their bytes are parameters, not extracted game resources.
    random=0x12345678
    for case in range(192):
        density=[10,25,40,65][case%4];fw,fh=[(1,1),(2,2),(1,2),(3,2)][case//4%4];blocked=[]
        for y in range(16):
            for x in range(16):
                random=(random*1664525+1013904223)&0xffffffff
                if random%100<density:blocked.append((x,y))
        add(f'random-{case}',16,16,fw,fh,(1,1),(13-fw,13-fh),blocked)
    return cases


def generate(game):
    report,code=analyze(game);o=SearchOracle(code,report['fs_assignments'])
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    o.cpu.mem_write(0x19c7,objects[1]['payload'][0x19c7:0x19e0])
    cases=make_cases();digest=hashlib.sha256();total=0;empty=0;partial=0
    for i,c in enumerate(cases):
        packed,route,found=o.search(c);digest.update(packed)
        c['sha256']=hashlib.sha256(packed).hexdigest()
        total+=len(o.trace)//3;empty+=not found
        x,y=c['start']
        for d in route:dx,dy=DELTAS[d];x+=dx;y+=dy
        partial+=found and [x,y]!=c['target']
        if i%32==0:print(i,c['name'],len(route),len(o.trace)//3,flush=True)
    return dict(format_version=1,source_sha256=report['source_sha256']['NORWIND.EXE'],cases=cases,
                result=dict(case_count=len(cases),probe_count=total,no_route_count=empty,partial_count=partial,sha256=digest.hexdigest()))

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',type=Path,required=True);p.add_argument('--fixture',type=Path,required=True);a=p.parse_args()
    result=generate(a.game);a.fixture.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(result['result'])
