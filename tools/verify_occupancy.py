#!/usr/bin/env python3
"""Execute original occupancy writers, classify initial WRK registration prefixes,
and reconstruct initial common-registration maps. No original bytes are exported.
"""
import argparse
from collections import Counter
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *
from analyze_executable import analyze
from import_game import decode_scenario, decode_templates, decode_objects, link_templates


class OccupancyOracle:
    CODE, POOL, DAT, STACK, WRK = 0x10000,0x400000,0x500000,0x600000,0x800000
    GRID,GDT,FS,PARTICIPANTS = 0x900000,0xa00000,0xb00000,0xd00000
    STOP=STACK+2048

    def __init__(self,code,assignments):
        self.cpu=Uc(UC_ARCH_X86,UC_MODE_32)
        for address,size in [(0,0x10000),(self.CODE,0x30000),(self.POOL,0x100000),(self.DAT,0x10000),
                             (self.STACK,0x1000),(self.WRK,0x10000),(self.GRID,0xc0000),
                             (self.GDT,0x1000),(self.FS,0x1000),(self.PARTICIPANTS,0x20000)]:
            self.cpu.mem_map(address,size)
        self.cpu.mem_write(self.CODE,code)
        self.cpu.mem_write(self.GDT+8,struct.pack('<HHBBBB',0xfff,self.FS&65535,(self.FS>>16)&255,0x93,0x40,self.FS>>24))
        self.cpu.mem_write(self.GDT+16,struct.pack('<HHBBBB',65535,0,0,0x93,0xcf,0))
        self.cpu.mem_write(self.GDT+24,struct.pack('<HHBBBB',65535,0,0,0x9b,0xcf,0))
        self.cpu.reg_write(UC_X86_REG_GDTR,(0,self.GDT,31,0))
        for register in [UC_X86_REG_DS,UC_X86_REG_ES,UC_X86_REG_SS]: self.cpu.reg_write(register,0x10)
        self.cpu.reg_write(UC_X86_REG_CS,0x18);self.cpu.reg_write(UC_X86_REG_FS,8)
        for item in assignments:
            self.cpu.mem_write(self.FS+item['fs_slot'],struct.pack('<I',self.CODE+item['target_offset']))
        self.cpu.mem_write(self.FS+8,struct.pack('<I',self.POOL))
        self.cpu.mem_write(self.FS+0x10,b'\x01');self.cpu.mem_write(0x19ea,b'\x01')
        self.cpu.mem_write(0x6ba,struct.pack('<I',self.PARTICIPANTS))
        self.mode='';self.goal=None;self.wrk_size=0
        self.cpu.hook_add(UC_HOOK_CODE,self.guard)

    def guard(self,cpu,address,size,user_data):
        if self.mode=='prefix':
            if address in [self.CODE+0x81d9,self.CODE+0xd2d1]:
                self.goal='overwrite' if address==self.CODE+0x81d9 else 'claim_empty'
                cpu.emu_stop();return
            allowed=[(self.WRK,self.WRK+self.wrk_size),(self.CODE+0x6877,self.CODE+0x68a0),
                     (self.CODE+0x1ead8,self.CODE+0x1eaed)]
        else:
            allowed=[(self.CODE+0x43b0,self.CODE+0x4442),(self.CODE+0x6885,self.CODE+0x68a0),
                     (self.CODE+0xd2d1,self.CODE+0xd363)]
        if not any(start<=address<end for start,end in allowed):
            raise ValueError(f'Unexpected occupancy control-flow escape: {address:#x}')

    def prepare(self,width,height):
        self.width,self.height=width,height
        size=width*height*4
        self.cpu.mem_write(0x18de,struct.pack('<II',width,height))
        for plane in range(3): self.cpu.mem_write(0x18ce+plane*4,struct.pack('<I',self.GRID+plane*size))
        self.cpu.mem_write(0x6aa,struct.pack('<I',self.POOL))
        self.cpu.mem_write(self.GRID,bytes(size*3))

    def start(self,address):
        self.cpu.mem_write(self.STACK+0xf00,struct.pack('<I',self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP,self.STACK+0xf00)
        self.cpu.reg_write(UC_X86_REG_EFLAGS,2)
        self.cpu.emu_start(address,self.STOP,count=10000)

    def prefix(self,raw,record,dat,wrk,entry,reference):
        if struct.unpack_from('<I',record,0x40)[0]&0x40000:
            return 'excluded_common_flag'
        self.mode='prefix';self.goal=None;self.wrk_size=len(wrk)
        self.cpu.mem_write(self.WRK,wrk);self.cpu.mem_write(self.DAT,dat)
        self.cpu.mem_write(0x6ca+raw[0]*0x56+4,struct.pack('<I',self.DAT+2))
        pointer=self.POOL+reference
        self.cpu.mem_write(pointer,raw)
        self.cpu.mem_write(0x273f,record[3:5]+b'\0\0')
        self.cpu.reg_write(UC_X86_REG_EAX,0x100);self.cpu.reg_write(UC_X86_REG_ESI,pointer)
        self.start(self.WRK+entry)
        if self.goal is None:
            if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP: raise ValueError('Prefix instruction limit reached')
            return 'deferred_first_update'
        after=bytes(self.cpu.mem_read(pointer,len(raw)))
        if after[4:14]!=raw[4:14]: raise ValueError('Initialization prefix changed spatial fields')
        if self.goal=='claim_empty':
            args=(self.cpu.reg_read(UC_X86_REG_EAX),self.cpu.reg_read(UC_X86_REG_EBX),self.cpu.reg_read(UC_X86_REG_ECX)&255)
            if args!=(reference,0,0): raise ValueError(f'Unsupported conditional registration arguments: {args}')
        return self.goal

    def write(self,raw,record,dat,reference,value,conditional=False,expected=0):
        self.mode='writer'
        self.cpu.mem_write(self.DAT,dat)
        self.cpu.mem_write(0x6ca+raw[0]*0x56+4,struct.pack('<I',self.DAT+2))
        pointer=self.POOL+reference;self.cpu.mem_write(pointer,raw)
        self.cpu.reg_write(UC_X86_REG_ESI,pointer)
        self.cpu.reg_write(UC_X86_REG_EAX,value)
        self.cpu.reg_write(UC_X86_REG_EBX,expected)
        self.cpu.reg_write(UC_X86_REG_ECX,0)  # D2D1 damage/reaction path disabled.
        saved=record[3:5]+b'\x55\x66' if not conditional else record[3:5]+b'\0\0'
        self.cpu.mem_write(0x273f,saved)
        self.start(self.CODE+(0xd2d1 if conditional else 0x43e5))
        if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP: raise ValueError('Writer did not return')
        if not conditional and bytes(self.cpu.mem_read(0x273f,4))!=saved: raise ValueError('43E5 did not restore footprint globals')
        return self.cpu.reg_read(UC_X86_REG_EAX) if conditional else 0

    def grid_bytes(self):
        return bytes(self.cpu.mem_read(self.GRID,3*self.width*self.height*4))


def synthetic_fixture(oracle):
    fixture={'format_version':1,'width':8,'height':8,'footprints':[[1,1],[4,3],[4,2],[3,3],[8,8]],
             'layers':[0,1,2,128,129,130],'values':[0,1,65537,4294967295],
             'patterns':[[0],[0,1,2,65537,4294967295]]}
    dat=bytearray(173);struct.pack_into('<H',dat,0,171)
    counts={};hashes={}
    for conditional in [False,True]:
        digest=hashlib.sha256();count=0
        for fw,fh in fixture['footprints']:
            dat[5],dat[6]=fw,fh
            for top in range(9-fh):
                for left in range(9-fw):
                    for layer,value,pattern in product(fixture['layers'],fixture['values'],fixture['patterns']):
                        for expected in (fixture['values'] if conditional else [0]):
                            oracle.prepare(8,8)
                            initial=[pattern[i%len(pattern)] for i in range(192)]
                            oracle.cpu.mem_write(oracle.GRID,struct.pack('<192I',*initial))
                            raw=bytearray(49);raw[13]=layer
                            struct.pack_into('<II',raw,4,(left+fw//2)*32,(top+fh//2)*32)
                            hits=oracle.write(bytes(raw),bytes(dat[2:]),bytes(dat),1,value,conditional,expected)
                            digest.update(oracle.grid_bytes())
                            if conditional:digest.update(struct.pack('<I',hits))
                            count+=1
        key='conditional' if conditional else 'overwrite'
        counts[key]=count;hashes[key]=digest.hexdigest();print(key,count,flush=True)
    fixture.update({'case_count':counts,'sha256':hashes,'conditional_mode':'CL=0; no victim damage'})
    return fixture


def generate(game):
    analysis,code=analyze(game)
    oracle=OccupancyOracle(code,analysis['fs_assignments'])
    fixture=synthetic_fixture(oracle)
    files={p.name.upper():p for p in game.iterdir() if p.is_file()}
    tables={n:decode_templates(p.read_bytes()) for n,p in files.items() if n.startswith('ARMY__') and n.endswith('.DAT')}
    profile={'format_version':1,'templates':{},'scenarios':{},'source_sha256':{}}
    total=Counter()
    for path in sorted(game.glob('SCENA_*.SCN')):
        scenario=decode_scenario(path.read_bytes());map_data=files[scenario['map_file']].read_bytes()
        width,height=struct.unpack_from('<II',map_data,17)
        cases=[];inputs={path.name,scenario['map_file'],'NORWIND.EXE'};counts=Counter()
        for slot in scenario['slots']:
            inputs.add(slot['object_file'])
            objects=decode_objects(files[slot['object_file']].read_bytes());link_templates(objects,tables,scenario['army_files'])
            for obj in objects:
                dat_name=obj['template_file'];wrk_name=dat_name.replace('.DAT','.WRK');inputs.update([dat_name,wrk_name])
                template=tables[dat_name]['templates'][obj['template_index']]
                raw=bytes.fromhex(obj['raw_record_hex']);record=bytes.fromhex(template['raw_record_hex'])
                reference=slot['slot_index']*65536+1+obj['file_offset']
                mode=oracle.prefix(raw,record,files[dat_name].read_bytes(),files[wrk_name].read_bytes(),template['wrk_entry_offset'],reference)
                key=f'{dat_name}:{obj["template_index"]}'
                if key in profile['templates'] and profile['templates'][key]!=mode:raise ValueError('Template registration varies across source records')
                profile['templates'][key]=mode
                cases.append((raw,record,dat_name,reference,mode));counts[mode]+=1
        oracle.prepare(width,height);conflicts=0
        for raw,record,dat_name,reference,mode in cases:
            if mode in ['overwrite','claim_empty']:
                conflicts+=oracle.write(raw,record,files[dat_name].read_bytes(),reference,reference,mode=='claim_empty',0)
        hashes={name:hashlib.sha256(files[name].read_bytes()).hexdigest() for name in sorted(inputs)}
        profile['source_sha256'].update(hashes)
        profile['scenarios'][path.name]={'source_files':sorted(inputs),'object_count':len(cases),'counts':dict(counts),
                                         'claim_conflicts':conflicts,'sha256':hashlib.sha256(oracle.grid_bytes()).hexdigest()}
        total.update(counts)
    profile['totals']=dict(total)
    profile['limitations']=['Initial common registration geometry, not a complete first game tick or AI/visibility/effect simulation.',
                            'Prefixes run with AH=1, initial serialized fields and FS counter=1; stop at the actual registration helper.',
                            'Template flag 0x40000 excludes the common 81D9 rectangle writer; special scripted effects remain outside this snapshot.',
                            'Two deferred records return without initial registration at this phase.',
                            'Conditional registration uses expected=0 and CL=0; damaging variants of D2D1 are not ported.']
    fixture['source_sha256']={'NORWIND.EXE':analysis['source_sha256']['NORWIND.EXE']}
    print('Source records',dict(total),flush=True)
    return fixture,profile


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,required=True)
    parser.add_argument('--profile',type=Path,required=True)
    args=parser.parse_args();fixture,profile=generate(args.game)
    for path,data in [(args.fixture,fixture),(args.profile,profile)]:
        path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__ == '__main__':main()
