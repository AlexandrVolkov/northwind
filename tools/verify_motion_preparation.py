#!/usr/bin/env python3
"""Execute original heading, turning, terrain preparation and their motion continuation."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ESI, UC_X86_REG_EIP, UC_X86_REG_EFLAGS, UC_X86_REG_ECX
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_spatial import SpatialOracle, record


class PreparationOracle(SpatialOracle):
    MAP,SEG=0xe00000,0xf00000

    def __init__(self,code,assignments):
        super().__init__(code,assignments)
        self.cpu.mem_map(self.MAP,0x1000);self.cpu.mem_map(self.SEG,0x1000)
        self.cpu.mem_write(self.MAP,struct.pack('<144I',*([0xabc00001]*144)))
        self.cpu.mem_write(0x18ee,struct.pack('<I',self.MAP));self.cpu.mem_write(0x18fe,struct.pack('<I',self.SEG))

    def guard(self,cpu,address,size,user_data):
        if self.mode=='initialize' and address==self.CODE+0x71f5:
            self.finished='initialized';cpu.emu_stop();return
        if self.mode=='combined' and address in [self.CODE+0x725c,self.CODE+0x72d8]:
            self.finished='motion' if address==self.CODE+0x725c else 'pending';cpu.emu_stop();return
        if self.mode=='combined' and address==self.CODE+0x9b0d:self.transferred=True
        ranges={'heading':[(0x9688,0x96cd)],'turn':[(0x9653,0x9688)],
                'initialize':[(0x7180,0x71f5)],'combined':[(0x7165,0x725c),(0x9653,0x9688),
                            (0x9b0d,0x9b86),(0x43b0,0x43e5),(0x4409,0x4473)]}
        if not any(self.CODE+lo<=address<self.CODE+hi for lo,hi in ranges[self.mode]):
            raise ValueError(f'Unexpected preparation control-flow escape: {address:#x}')

    def setup(self,raw,template):
        self.cpu.mem_write(self.POOL+65537,raw);self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+65537)
        self.cpu.mem_write(self.DAT,template);self.cpu.mem_write(0x1c49,struct.pack('<I',self.DAT))

    def read_record(self):return bytes(self.cpu.mem_read(self.POOL+65537,64))


def template(base=0,stride=0,value=0,fw=1,fh=1):
    raw=bytearray(171);raw[3]=fw;raw[4]=fh
    struct.pack_into('<H',raw,0x48,base);raw[0x4a]=stride
    raw[0x58:0x60]=bytes((value+13*i)&255 for i in range(8))
    return bytes(raw)


def generate(game):
    report,code=analyze(game);oracle=PreparationOracle(code,report['fs_assignments'])
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    selector=objects[1]['payload'][0x19d7:0x19e0]
    if list(selector)!=[7,6,5,0,255,4,1,2,3]:raise ValueError('Unexpected heading table')
    oracle.cpu.mem_write(0x19c7,objects[1]['payload'][0x19c7:0x19e0])
    fixture={'format_version':1,'source_sha256':{'NORWIND.EXE':report['source_sha256']['NORWIND.EXE']},
             'coordinates':[0,1,2,15,16,31,32,63,64,127,128,2147483647,2147483648,4294967294,4294967295],
             'turn':{'directions':list(range(256)),'targets':list(range(32))+[63,127,255],
                     'bases':[0,65530,65535],'strides':[0,1,32,255]},
             'initialization_profiles':[
                 {'values':list(range(256)),'properties':list(range(256)),'prior':[191],'modifiers':[255],'layers':[0],'occupied':[0]},
                 {'values':[0,1,15,63,64,127,128,129,191,192,255],'properties':list(range(0,256,17)),
                  'prior':[0,63,64,128,192,255],'modifiers':[0,31,32,224,255],'layers':[0,1,2,129],'occupied':[0,65537]}],
             'combined':{'directions':[0,2,18,19,20,24,27,63,128,255],'bases':[0,65535],
                         'strides':[0,1,255],'values':[1,63,128,255],'remaining':[16,17,32],
                         'layers':[0,1,2],'patterns':[[0],[0,1,65537,4294967295]],
                         'phases':[0,16],'footprints':[[1,1],[4,3]]}}
    digest=hashlib.sha256();count=0;oracle.mode='heading'
    for x,y,tx,ty in product(fixture['coordinates'],repeat=4):
        raw=record(64,0,0,x,y);oracle.setup(bytes(raw),template())
        oracle.cpu.reg_write(UC_X86_REG_EAX,tx);oracle.cpu.reg_write(UC_X86_REG_EBX,ty)
        oracle.start(oracle.CODE+0x9688)
        if oracle.cpu.reg_read(UC_X86_REG_EIP)!=oracle.STOP or oracle.read_record()!=raw:raise ValueError('Invalid heading return')
        digest.update(bytes([oracle.cpu.reg_read(UC_X86_REG_EAX)&255]));count+=1
    fixture['heading_case_count']=count;fixture['heading_sha256']=digest.hexdigest();print('Heading',count,flush=True)
    digest=hashlib.sha256();count=0;oracle.mode='turn';spec=fixture['turn']
    for direction,target,base,stride in product(*(spec[k] for k in ['directions','targets','bases','strides'])):
        raw=record(64,0,0);raw[0x25]=direction;struct.pack_into('<H',raw,1,0xbeef)
        oracle.setup(bytes(raw),template(base,stride));oracle.cpu.reg_write(UC_X86_REG_EAX,target<<8)
        oracle.start(oracle.CODE+0x9653)
        if oracle.cpu.reg_read(UC_X86_REG_EIP)!=oracle.STOP:raise ValueError('Invalid turn return')
        digest.update(oracle.read_record()+bytes([bool(oracle.cpu.reg_read(UC_X86_REG_EFLAGS)&64)]));count+=1
    fixture['turn_case_count']=count;fixture['turn_sha256']=digest.hexdigest();print('Turn',count,flush=True)
    digest=hashlib.sha256();count=0;oracle.mode='initialize';oracle.prepare(12,12)
    for profile in fixture['initialization_profiles']:
        for value,prop,prior,modifier,layer,occupied in product(*(profile[k] for k in ['values','properties','prior','modifiers','layers','occupied'])):
            raw=record(64,0,layer);raw[0x26]=prior;raw[0x16]=modifier
            oracle.setup(bytes(raw),template(value=value));oracle.cpu.mem_write(oracle.SEG+8,bytes([prop]))
            oracle.cpu.mem_write(oracle.GRID+(6*12+5)*4,struct.pack('<I',occupied))
            oracle.finished=None;oracle.start(oracle.CODE+0x7180)
            if oracle.finished!='initialized':raise ValueError('Initialization did not reach its boundary')
            digest.update(oracle.read_record());count+=1
    fixture['initialize_case_count']=count;fixture['initialize_sha256']=digest.hexdigest();print('Initialize',count,flush=True)
    digest=hashlib.sha256();count=0;oracle.mode='combined';spec=fixture['combined']
    for direction,base,stride,value,remaining,layer,pattern,phase,footprint in product(
            *(spec[k] for k in ['directions','bases','strides','values','remaining','layers','patterns','phases','footprints'])):
        oracle.seed(12,12,pattern,*footprint)
        raw=record(64,0,layer,160+phase,192+phase);raw[0x25]=direction;raw[0x2c]=remaining
        oracle.setup(bytes(raw),template(base,stride,value,*footprint));oracle.cpu.mem_write(oracle.SEG+8,b'\x91')
        oracle.cpu.mem_write(0x1c9f,b'\xff');oracle.finished=None;oracle.transferred=False
        oracle.start(oracle.CODE+0x7165)
        if oracle.finished not in ['motion','pending']:raise ValueError('Combined motion did not reach its boundary')
        entered=oracle.finished=='motion';distance=oracle.cpu.reg_read(UC_X86_REG_ECX) if entered else 0
        result=oracle.read_record()+bytes(oracle.cpu.mem_read(0x1c9f,1))+struct.pack('<BIB',entered,distance,oracle.transferred)
        digest.update(result+oracle.grid_bytes());count+=1
    fixture['combined_case_count']=count;fixture['combined_sha256']=digest.hexdigest();print('Combined',count,flush=True)
    return fixture


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,required=True);args=parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game),indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':main()
