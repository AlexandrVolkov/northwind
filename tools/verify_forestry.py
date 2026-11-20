#!/usr/bin/env python3
"""Execute original grove lookup, harvest micro-operations and terrain replacement."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from itertools import product
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle
from forestry_source import source_constants


class ForestryOracle(OccupancyOracle):
    MAP,SEG=0xe00000,0xf00000
    def __init__(self,code,assignments,constants):
        super().__init__(code,assignments)
        self.cpu.mem_map(self.MAP,0x40000);self.cpu.mem_map(self.SEG,0x10000)
        self.cpu.mem_write(0x7c0,struct.pack('<9I',*constants['patch_masks']))
        self.cpu.mem_write(0x297e,bytes(constants['random_values']))
        self.cpu.mem_write(0x1906,b'\xff');self.cpu.mem_write(0x190a,b'\xff') # viewport dirty cache outside map

    def guard(self,cpu,address,size,user_data):
        if self.mode in ['work','prepare'] and address in [self.CODE+0x84e4,self.CODE+0x85cc]:
            self.boundary=address-self.CODE;cpu.emu_stop();return
        if self.mode=='work' and address==self.CODE+0x8596:self.swing=True
        ranges=[(0xcd72,0xd12c),(0x9e41,0x9e55)]
        if self.mode=='find':ranges+=[(0xc35f,0xc439)]
        if self.mode=='work':ranges+=[(0x8585,0x85cc),(0x8af4,0x8b0f),(0x8c89,0x8c98)]
        if self.mode=='modifier':ranges+=[(0x85d5,0x85e2)]
        if self.mode=='prepare':ranges+=[(0x8542,0x8554),(0x84e1,0x84e4)]
        if not any(self.CODE+a<=address<self.CODE+b for a,b in ranges):raise ValueError(f'Forestry escape: {address:#x}')

    def terrain(self,width,height,cells,properties,random_index=0):
        self.width,self.height=width,height
        self.cpu.mem_write(0x18de,struct.pack('<II',width,height))
        self.cpu.mem_write(0x18ee,struct.pack('<I',self.MAP));self.cpu.mem_write(0x18fe,struct.pack('<I',self.SEG))
        self.cpu.mem_write(0x1902,struct.pack('<I',len(properties)//8))
        self.cpu.mem_write(self.MAP,struct.pack(f'<{len(cells)}I',*cells));self.cpu.mem_write(self.SEG,properties)
        self.cpu.mem_write(0x19eb,bytes([random_index]))

    def map_bytes(self):return bytes(self.cpu.mem_read(self.MAP,self.width*self.height*4))

    def start(self,address):
        self.cpu.mem_write(self.STACK+0xf00,struct.pack('<I',self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP,self.STACK+0xf00);self.cpu.reg_write(UC_X86_REG_EFLAGS,2)
        self.cpu.emu_start(address,self.STOP,count=2000000)

    def clear(self,x,y,ax=0x201):
        self.mode='clear';self.cpu.reg_write(UC_X86_REG_EAX,ax)
        self.cpu.reg_write(UC_X86_REG_ECX,0x101);self.cpu.reg_write(UC_X86_REG_EDX,x|(y<<8))
        self.start(self.CODE+0xcfac)
        if self.cpu.reg_read(UC_X86_REG_EIP)!=self.STOP:raise ValueError('Terrain replacement did not return')
        return self.map_bytes()+bytes(self.cpu.mem_read(0x19eb,1))

    def worker(self,raw,template):
        self.cpu.mem_write(self.POOL+1,raw);self.cpu.reg_write(UC_X86_REG_ESI,self.POOL+1)
        self.cpu.mem_write(self.DAT,template);self.cpu.mem_write(0x1c49,struct.pack('<I',self.DAT))


def seed_record():
    raw=bytearray((i*13+5)&255 for i in range(54));raw[0x32]=5;raw[0x33]=3
    return raw


def synthetic_catalog(kind):
    descriptors=[]
    for bits in range(16):
        corners=sum((6 if bits&(1<<i) else 4)<<(i*4) for i in range(4))
        descriptors.append((corners<<16)|(1 if bits else 0))
    if kind==1:descriptors[10]=0x44440300 # force some atomic blend failures
    if kind==2:
        while len(descriptors)<260:descriptors.append(0x11110300)
    if kind==3:   # burnt (subtype 1) and crater (subtype 3) surfaces for CFAC with AX=101h and AX=300h
        descriptors.extend([0x44440110,0x66660110,0x66440110,0x44440300,0x66660300,0x44440300])
    descriptors.extend([0x44440210,0x44440210])
    return b''.join(struct.pack('<II',d,0) for d in descriptors)


def generate(game):
    constants=source_constants(game);report,code=analyze(game);oracle=ForestryOracle(code,report['fs_assignments'],constants)
    fixture={'format_version':1,'source_sha256':constants['source_sha256'],
             'query_high':[0,65535,18058],
             'find':{'patterns':[[0,1,0,2],[0],[3,2,0,1]],'descriptors':[0,1,257,0x66660011],
                     'footprints':[[1,1],[4,3]],'radii':[1,3,10]},
             'work':{'modifiers':[0,31,32,127,128,250,255],'counters':[0,1,7,8,9,127,255],'bases':[0,1,8,255]},
             'prepare':{'hits':[0,1,20,31,32,255],'auxiliary':[0,1,255]},
             'clear':{'catalogs':[0,1,2],'random_indices':[0,1,127,255]},'results':{}}
    def save(name,digest,count):
        fixture['results'][name]={'case_count':count,'sha256':digest.hexdigest()};print(name,count,flush=True)
    digest=hashlib.sha256();count=0;oracle.mode='query';oracle.terrain(8,8,[0xabc00000]*64,bytes(8))
    for high in fixture['query_high']:
        for low in range(65536):
            oracle.cpu.mem_write(oracle.SEG,struct.pack('<I',(high<<16)|low));oracle.cpu.reg_write(UC_X86_REG_ECX,0x305)
            oracle.start(oracle.CODE+0xd101)
            if oracle.cpu.reg_read(UC_X86_REG_EIP)!=oracle.STOP:raise ValueError('Query failed to return')
            digest.update(struct.pack('<I',oracle.cpu.reg_read(UC_X86_REG_EAX)));count+=1
    save('query',digest,count)
    digest=hashlib.sha256();count=0;oracle.mode='find';spec=fixture['find'];template=bytearray(171)
    properties=b''.join(struct.pack('<II',d,0) for d in spec['descriptors'])
    for pattern,footprint,radius,y,x in product(spec['patterns'],spec['footprints'],spec['radii'],range(8),range(8)):
        if x<footprint[0]//2 or y<footprint[1]//2 or x-footprint[0]//2+footprint[0]>8 or y-footprint[1]//2+footprint[1]>8:continue
        oracle.terrain(8,8,[0xabc00000|pattern[i%len(pattern)] for i in range(64)],properties)
        raw=seed_record();struct.pack_into('<II',raw,4,x*32+16,y*32+16);oracle.worker(bytes(raw),bytes(template))
        oracle.cpu.mem_write(0x273f,bytes(footprint)+b'\0\0');oracle.cpu.reg_write(UC_X86_REG_EAX,1);oracle.cpu.reg_write(UC_X86_REG_EDX,radius)
        oracle.start(oracle.CODE+0xc35f)
        if oracle.cpu.reg_read(UC_X86_REG_EIP)!=oracle.STOP:raise ValueError('Search failed to return')
        found=not bool(oracle.cpu.reg_read(UC_X86_REG_EFLAGS)&64)
        digest.update(bytes([found,oracle.cpu.reg_read(UC_X86_REG_EAX)&255 if found else 255,oracle.cpu.reg_read(UC_X86_REG_EBX)&255 if found else 255]));count+=1
    save('find',digest,count)
    digest=hashlib.sha256();count=0;oracle.mode='prepare'
    for packed,hits,aux in product(range(256),fixture['prepare']['hits'],fixture['prepare']['auxiliary']):
        raw=seed_record();raw[0x34]=packed;raw[0x15]=aux;template[0x78]=hits
        oracle.worker(bytes(raw),bytes(template));oracle.boundary=None;oracle.start(oracle.CODE+0x8542)
        if oracle.boundary!=0x84e4:raise ValueError('Work preparation escaped')
        digest.update(bytes(oracle.cpu.mem_read(oracle.POOL+1,54)));count+=1
    save('prepare',digest,count)
    digest=hashlib.sha256();count=0;oracle.mode='work'
    for packed,modifier,counter,base in product([i for i in range(256) if i&31],fixture['work']['modifiers'],fixture['work']['counters'],fixture['work']['bases']):
        raw=seed_record();raw[0x34]=packed;raw[0x16]=modifier;raw[0x2f]=counter;template[0x75]=base
        oracle.worker(bytes(raw),bytes(template));oracle.boundary=None;oracle.swing=False;oracle.start(oracle.CODE+0x8585)
        if oracle.boundary!=0x84e4:raise ValueError('Pending work escaped')
        digest.update(bytes(oracle.cpu.mem_read(oracle.POOL+1,54))+bytes([oracle.swing]));count+=1
    save('work',digest,count)
    digest=hashlib.sha256();count=0;oracle.mode='modifier'
    for modifier in range(256):
        raw=seed_record();raw[0x16]=modifier;oracle.worker(bytes(raw),bytes(template));oracle.start(oracle.CODE+0x85d5)
        if oracle.cpu.reg_read(UC_X86_REG_EIP)!=oracle.STOP:raise ValueError('Modifier failed to return')
        digest.update(bytes(oracle.cpu.mem_read(oracle.POOL+1,54)));count+=1
    save('modifier',digest,count)
    digest=hashlib.sha256();count=0
    for kind,random_index,y,x in product(fixture['clear']['catalogs'],fixture['clear']['random_indices'],range(8),range(8)):
        oracle.terrain(8,8,[0xabc00000|((i*7+5)%16) for i in range(64)],synthetic_catalog(kind),random_index)
        digest.update(oracle.clear(x,y));count+=1
    save('clear',digest,count)
    digest=hashlib.sha256();count=0
    for kind,random_index,packed in product(fixture['clear']['catalogs'],fixture['clear']['random_indices'],range(0,256,32)):
        oracle.terrain(8,8,[0xabc00000|((i*7+5)%16) for i in range(64)],synthetic_catalog(kind),random_index)
        raw=seed_record();raw[0x34]=packed;oracle.worker(bytes(raw),bytes(template));oracle.mode='work';oracle.boundary=None
        oracle.start(oracle.CODE+0x85a9)
        if oracle.boundary!=0x85cc:raise ValueError('Load preparation escaped')
        digest.update(bytes(oracle.cpu.mem_read(oracle.POOL+1,54))+oracle.map_bytes()+bytes(oracle.cpu.mem_read(0x19eb,1)));count+=1
    save('finish',digest,count)
    m=json.loads((Path(__file__).resolve().parents[1]/'assets/imported/map.json').read_text());properties=bytes.fromhex(m['segment_properties_hex']);cells=m['cells']
    eligible=[(i%m['width'],i//m['width']) for i,v in enumerate(cells) if struct.unpack_from('<I',properties,(v&65535)*8)[0]&0xf07==1]
    points=list(dict.fromkeys([(6,10),(9,20),(10,20)]+eligible[::max(1,len(eligible)//16)]))
    fixture['source_map']={'cells_sha256':hashlib.sha256(struct.pack(f'<{len(cells)}I',*cells)).hexdigest(),
                           'properties_sha256':hashlib.sha256(properties).hexdigest(),'points':points}
    digest=hashlib.sha256();count=0
    for x,y in points:
        for random_index in fixture['clear']['random_indices']:
            oracle.terrain(m['width'],m['height'],cells,properties,random_index);digest.update(oracle.clear(x,y));count+=1
    save('source_map',digest,count)
    # CFAC as the fire (AX=101h: burnt ground) and the catapult (AX=300h: crater) call it.
    fixture['scorch']={'catalogs':[0,1,3],'modes':[0x101,0x300],'random_indices':[0,1,127,255]}
    digest=hashlib.sha256();count=0
    for kind,ax,random_index,y,x in product(fixture['scorch']['catalogs'],fixture['scorch']['modes'],fixture['scorch']['random_indices'],range(8),range(8)):
        oracle.terrain(8,8,[0xabc00000|((i*7+5)%16) for i in range(64)],synthetic_catalog(kind),random_index)
        digest.update(oracle.clear(x,y,ax));count+=1
    save('scorch',digest,count)
    return fixture


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--probe',action='store_true');parser.add_argument('--fixture',type=Path);args=parser.parse_args()
    if args.fixture:
        args.fixture.write_text(json.dumps(generate(args.game),indent=2)+'\n',encoding='utf-8');return
    constants=source_constants(args.game);report,code=analyze(args.game)
    oracle=ForestryOracle(code,report['fs_assignments'],constants)
    scenario=json.loads((Path(__file__).resolve().parents[1]/'assets/imported/map.json').read_text())
    properties=bytes.fromhex(scenario['segment_properties_hex']);cells=scenario['cells']
    oracle.terrain(scenario['width'],scenario['height'],cells,properties)
    result=oracle.clear(10,20);actual=list(struct.unpack(f'<{len(cells)}I',result[:-1]))
    changed=[(i%scenario['width'],i//scenario['width'],a,b) for i,(a,b) in enumerate(zip(cells,actual)) if a!=b]
    print('Changed',changed,'RNG',result[-1])


if __name__=='__main__':main()
