#!/usr/bin/env python3
"""Compare bounded movement and full forward-chain detachment with original x86."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ESI, UC_X86_REG_EIP, UC_X86_REG_ECX
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_occupancy import OccupancyOracle


class SpatialOracle(OccupancyOracle):
    def guard(self,cpu,address,size,user_data):
        if self.mode=='motion' and address==self.CODE+0x725c:
            self.finished=True;cpu.emu_stop();return
        if self.mode=='motion' and address==self.CODE+0x9b0d:self.transferred=True
        allowed=[(0x43b0,0x43e5),(0x4409,0x4473)]
        if self.mode=='motion':allowed += [(0x71f5,0x725c),(0x9b0d,0x9b86)]
        elif self.mode=='detach':allowed += [(0x98cc,0x996f),(0x6877,0x68a0)]
        else:raise ValueError('Unexpected oracle mode')
        if not any(self.CODE+lo<=address<self.CODE+hi for lo,hi in allowed):
            raise ValueError(f'Unexpected spatial control-flow escape: {address:#x}')

    def seed(self,width,height,pattern,fw,fh):
        self.prepare(width,height)
        initial=[pattern[i%len(pattern)] for i in range(width*height*3)]
        self.cpu.mem_write(self.GRID,struct.pack(f'<{len(initial)}I',*initial))
        self.cpu.mem_write(0x273f,bytes([fw,fh,0x55,0x66]))

    def motion(self,raw,reference):
        self.mode='motion';self.finished=False;self.transferred=False
        pointer=self.POOL+reference
        self.cpu.mem_write(pointer,raw);self.cpu.reg_write(UC_X86_REG_ESI,pointer)
        self.cpu.mem_write(0x1c9f,b'\xff')
        self.start(self.CODE+0x71f5)
        if not self.finished:raise ValueError('Motion instruction limit reached')
        result=bytes(self.cpu.mem_read(pointer,len(raw)))+bytes(self.cpu.mem_read(0x1c9f,1))
        result+=struct.pack('<IB',self.cpu.reg_read(UC_X86_REG_ECX),self.transferred)
        return result+self.grid_bytes()

    def detach(self,pool,base,reference,cursor,owner,flags,counters,fw,fh,end=None):
        self.mode='detach'
        self.cpu.mem_write(self.POOL+base,pool)
        end=base+len(pool) if end is None else end
        descriptor=0x97a+owner*0xf5
        self.cpu.mem_write(descriptor+5,struct.pack('<I',self.POOL+base))
        self.cpu.mem_write(descriptor+9,struct.pack('<I',end))
        self.cpu.mem_write(descriptor+14,struct.pack('<I',cursor))
        dat=bytearray(173);struct.pack_into('<H',dat,0,171)
        dat[5],dat[6]=fw,fh;struct.pack_into('<I',dat,2+0x40,flags)
        self.cpu.mem_write(self.DAT,bytes(dat));self.cpu.mem_write(0x6ce,struct.pack('<I',self.DAT+2))
        participant=self.PARTICIPANTS+owner*0x1a35
        self.cpu.mem_write(participant+0x19f5,struct.pack('<II',*counters))
        self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+reference)
        self.start(self.CODE+0x98cc)
        if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP:raise ValueError('Detachment did not return')
        actual_base=struct.unpack('<I',self.cpu.mem_read(descriptor+5,4))[0]-self.POOL
        result=bytes(self.cpu.mem_read(self.POOL+base,len(pool)))
        result+=struct.pack('<I',actual_base)+bytes(self.cpu.mem_read(descriptor+9,4))+bytes(self.cpu.mem_read(descriptor+14,4))
        result+=bytes(self.cpu.mem_read(participant+0x19f5,8))
        return result+self.grid_bytes()


def record(size,owner,layer,x=176,y=208):
    raw=bytearray((i*17+3)&255 for i in range(size))
    raw[0]=0;raw[3]=owner;raw[13]=layer;raw[14]=0
    struct.pack_into('<II',raw,4,x,y);struct.pack_into('<I',raw,16,size)
    return raw


def generate(game):
    report,code=analyze(game)
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    directions=list(struct.iter_unpack('<bb',objects[1]['payload'][0x19c7:0x19d7]))
    if directions!=[(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1)]:
        raise ValueError('Unexpected native direction table')
    oracle=SpatialOracle(code,report['fs_assignments'])
    oracle.cpu.mem_write(0x19c7,objects[1]['payload'][0x19c7:0x19d7])
    fixture={'format_version':1,'source_sha256':{'NORWIND.EXE':report['source_sha256']['NORWIND.EXE']},
             'width':12,'height':12,'reference':65537,'motion_profiles':[
                 {'speeds':list(range(256)),'remaining':list(range(33)),'directions':[i*9 for i in range(8)],
                  'footprints':[[1,1]],'layers':[0],'patterns':[[0]],'phases':[0]},
                 {'speeds':[3,63,255],'remaining':[15,16,17,32],'directions':list(range(64)),
                  'footprints':[[1,1],[4,3],[4,2]],'layers':[0,1,2,129],
                  'patterns':[[0],[0,1,65537,4294967295]],'phases':[0,16]}],
             'detach':{'sizes':[49,64,34,80,49],'lengths':[1,2,3,5],'bases':[1,65537],
                       'owners':[0,3],'layers':[0,1,2,129],'footprints':[[1,1],[4,3]],
                       'template_flags':[0,8,32,40],'record_flags':[0,128,512,65535],
                       'counters':[[0,0],[4294967295,4294967295]],'pattern':[0,1,65537,4294967295]}}
    digest=hashlib.sha256();count=0
    for profile in fixture['motion_profiles']:
        for speed,remaining,direction,footprint,layer,pattern,phase in product(
                *(profile[k] for k in ['speeds','remaining','directions','footprints','layers','patterns','phases'])):
            oracle.seed(12,12,pattern,*footprint)
            raw=record(64,0,layer,160+phase,192+phase)
            raw[0x25]=direction;raw[0x26]=speed;raw[0x2c]=remaining
            digest.update(oracle.motion(bytes(raw),fixture['reference']));count+=1
        print('Motion cases',count,flush=True)
    fixture['motion_case_count']=count;fixture['motion_sha256']=digest.hexdigest()
    digest=hashlib.sha256();count=0;spec=fixture['detach']
    for length in spec['lengths']:
        for removed in range(length):
            for cursor_index in range(removed+1):
                for base,owner,layer,footprint,flags,record_flags,counters in product(
                        *(spec[k] for k in ['bases','owners','layers','footprints','template_flags','record_flags','counters'])):
                    chunks=[record(size,owner,layer) for size in spec['sizes'][:length]]
                    for raw in chunks:struct.pack_into('<H',raw,0x17,record_flags)
                    reference=base+sum(spec['sizes'][:removed]);cursor=base+sum(spec['sizes'][:cursor_index])
                    oracle.seed(12,12,spec['pattern'],*footprint)
                    digest.update(oracle.detach(b''.join(chunks),base,reference,cursor,owner,flags,counters,*footprint));count+=1
    fixture['detach_case_count']=count;fixture['detach_sha256']=digest.hexdigest()
    print('Detach cases',count,flush=True)
    fixture['sequences']=[[1,3,0,2,4],[4,0,3,1,2]]
    digest=hashlib.sha256();count=0
    for order,base,flags in product(fixture['sequences'],spec['bases'],[0,8,40]):
        chunks=[record(size,0,1) for size in spec['sizes']]
        for raw in chunks:struct.pack_into('<H',raw,0x17,0)
        pool=b''.join(chunks);end=base+len(pool);cursor=base;counters=[0,0]
        oracle.seed(12,12,spec['pattern'],4,3)
        for index in order:
            reference=base+sum(spec['sizes'][:index])
            result=oracle.detach(pool,base,reference,cursor,0,flags,counters,4,3,end)
            digest.update(result);count+=1
            metadata=len(pool)
            actual_base,end,cursor,*counters=struct.unpack_from('<5I',result,metadata)
            if actual_base!=base:raise ValueError('Native detachment moved the allocation base')
            pool=result[:metadata]
    fixture['sequence_case_count']=count;fixture['sequence_sha256']=digest.hexdigest()
    print('Sequential detach steps',count,flush=True)
    return fixture


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game',type=Path,required=True);parser.add_argument('--fixture',type=Path,required=True)
    args=parser.parse_args();fixture=generate(args.game)
    args.fixture.write_text(json.dumps(fixture,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':main()
