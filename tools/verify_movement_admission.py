#!/usr/bin/env python3
"""Execute original 7C63 candidate checks after its caller clears the mover footprint."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_occupancy import OccupancyOracle


class AdmissionOracle(OccupancyOracle):
    MAP, SEG = 0xe00000, 0xf00000
    def __init__(self,code,assignments):
        super().__init__(code,assignments)
        self.cpu.mem_map(self.MAP,0x40000);self.cpu.mem_map(self.SEG,0x10000)
    support_override=None
    def guard(self,cpu,address,size,user_data):
        if not any(self.CODE+a<=address<self.CODE+b for a,b in [(0x7c63,0x7f34),(0x43b0,0x43e5),(0x4409,0x4473)]):
            raise ValueError(f'Admission escape: {address:#x}')
    def execute(self,address):
        self.start(self.CODE+address)
        if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP:raise ValueError('Admission instruction limit reached')
    def probe(self,fw,fh,x,y,layer,direction,terrain,values,pattern,ground,obstacle,mask=0,field=9):
        self.prepare(8,8)
        raw=bytearray(54);raw[0]=2;raw[3]=2;raw[0x0d]=layer;raw[0x19]=5
        struct.pack_into('<II',raw,4,x*32+16,y*32+16)
        own=bytearray(171);own[3:5]=bytes([fw,fh]);own[0x3f]=field;own[0x58:0x68]=bytes(values)
        templates=[own]
        for ref,index,owner,flags,rank,condition,threshold,state,phase,heading,work in [
                (100,1,*obstacle),(200,2,1,0,9,70,10,0x22,4,4,0),(300,3,0,10,60,20,15,0,0,0,0)]:
            obj=bytearray(54);obj[0]=2;obj[3]=owner;obj[0x0e]=index;obj[0x19]=condition;obj[0x2d]=state;obj[0x24]=phase;obj[0x25]=heading;obj[0x14]=work
            if ref==300 and self.support_override:
                flags,rank,condition,threshold=self.support_override;obj[0x19]=condition
            self.cpu.mem_write(self.POOL+ref,bytes(obj));t=bytearray(171);t[0x3f]=rank;t[0x46]=threshold;struct.pack_into('<I',t,0x40,flags);templates.append(t)
        self.cpu.mem_write(self.DAT,b''.join(templates));self.cpu.mem_write(0x6ca+2*0x56+4,struct.pack('<I',self.DAT))
        self.cpu.mem_write(0x98c+2*0xf5,struct.pack('<I',mask));self.cpu.mem_write(self.POOL+1,bytes(raw));self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+1)
        cells=[i%len(terrain) for i in range(64)];properties=b''.join(bytes([v])+bytes(7) for v in terrain)
        self.cpu.mem_write(self.MAP,struct.pack('<64I',*cells));self.cpu.mem_write(self.SEG,properties)
        self.cpu.mem_write(0x18ee,struct.pack('<I',self.MAP));self.cpu.mem_write(0x18fe,struct.pack('<I',self.SEG))
        grid=[0]*192
        if ground:grid[:64]=[300]*64
        grid[(layer&3)*64:((layer&3)+1)*64]=[pattern[i%len(pattern)] for i in range(64)]
        left,top=x-fw//2,y-fh//2
        for yy in range(top,top+fh):
            for xx in range(left,left+fw):grid[(layer&3)*64+yy*8+xx]=1
        self.cpu.mem_write(self.GRID,struct.pack('<192I',*grid))
        self.cpu.mem_write(0x273f,bytes([fw,fh]));self.cpu.reg_write(UC_X86_REG_EAX,0);self.execute(0x43b0)
        self.cpu.reg_write(UC_X86_REG_EAX,direction);self.execute(0x7c63)
        if bytes(self.cpu.mem_read(self.POOL+1,54))!=raw:raise ValueError('Candidate check changed mover record')
        return bytes([self.cpu.reg_read(UC_X86_REG_EBX)&255,self.cpu.reg_read(UC_X86_REG_EFLAGS)&1])


def generate(game):
    report,code=analyze(game);o=AdmissionOracle(code,report['fs_assignments'])
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ);o.cpu.mem_write(0x19c7,objects[1]['payload'][0x19c7:0x19d7])
    result={'format_version':1,'source_sha256':report['source_sha256']['NORWIND.EXE'],'results':{}}
    ordinary=[0,0,9,70,10,0x22,4,0,0]
    def save(name,digest,count):result['results'][name]={'case_count':count,'sha256':digest.hexdigest()};print(name,count,flush=True)
    digest=hashlib.sha256();count=0
    for terrain,value in product(range(256),repeat=2):
        values=[8]*16;values[terrain&15]=value
        digest.update(o.probe(1,1,3,3,1,2,[terrain],values,[0],False,ordinary));count+=1
    save('terrain',digest,count)
    # Ground-plane object occupies reference 300 so its condition/flags are freely varied.
    result['support']={'terrain':[0,2,8,15,128,255],'values':[0,1,8,127,128,129,255],'layers':[0,1,2],
        'flags':[0,2],'conditions':[0,15,16,127,128,255],'fields':[8,9,10],'thresholds':[15,128]}
    digest=hashlib.sha256();count=0;s=result['support']
    for terrain,value,layer,flags,condition,field,threshold in product(*(s[k]for k in ['terrain','values','layers','flags','conditions','fields','thresholds'])):
        values=[8]*16;values[terrain&15]=value
        # Keep active plane clear, put the variable support into plane zero via a temporary probe profile.
        # Reference 300 is the support slot; its descriptor is overridden by copying the variable record/template.
        o.support_override=(flags,field,condition,threshold)
        digest.update(o.probe(1,1,3,3,layer,2,[terrain],values,[0],True,ordinary));count+=1
    o.support_override=None
    save('support',digest,count)
    result['occupants']={'owners':[0,1,31],'masks':[0,1,2147483648,4294967295],'mover_fields':[0,9,255],
        'other_fields':[0,1,9,10,20,255],'flags':[0,8],'work':[0,64],'states':[0,32,34,47],'phases':[0,2,4],'headings':[0,4,63],'directions':[0,2,4]}
    digest=hashlib.sha256();count=0;s=result['occupants']
    for owner,mask,field,other,flags,work,state,phase,heading,direction in product(*(s[k]for k in ['owners','masks','mover_fields','other_fields','flags','work','states','phases','headings','directions'])):
        obstacle=[owner,flags,other,70,10,state,phase,heading,work]
        digest.update(o.probe(1,1,3,3,1,direction,[0],[8]*16,[100],False,obstacle,mask,field));count+=1
    save('occupants',digest,count)
    result['footprints']={'sizes':[[1,1],[2,2],[3,2]],'patterns':[[0],[0,100],[100,200],[200,100],[0,300]],'terrain':[[0],[0,2,3]],'layers':[0,1,2]}
    digest=hashlib.sha256();count=0;s=result['footprints']
    for fw,fh in s['sizes']:
        for y,x,direction,pattern,terrain,layer,ground in product(range(fh//2,8-fh+fh//2+1),range(fw//2,8-fw+fw//2+1),range(8),s['patterns'],s['terrain'],s['layers'],[False,True]):
            digest.update(o.probe(fw,fh,x,y,layer,direction,terrain,[8]*16,pattern,ground,[0,0,9,70,10,0x22,4,0,0]));count+=1
    save('footprints',digest,count)
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',type=Path,required=True);p.add_argument('--fixture',type=Path,required=True);a=p.parse_args()
    a.fixture.write_text(json.dumps(generate(a.game),indent=2)+'\n',encoding='utf-8')
