#!/usr/bin/env python3
"""Run original construction debit prefix and complete placement function.

Payment stops before B0E9 on success or before B1E4 cleanup on rejection.
Placement executes AD5F..AE39 and its real template lookup, with no stubs.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import (UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_EDX,
                              UC_X86_REG_EDI, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS)
from analyze_executable import analyze
from verify_building_kernel import OriginalKernel


class ConstructionOracle(OriginalKernel):
    MAP, PROPERTIES, OCCUPANCY = 0x900000, 0x910000, 0x920000

    def __init__(self, code):
        super().__init__(code)
        self.mode = ''
        self.rejected = False
        for address in [self.MAP,self.PROPERTIES,self.OCCUPANCY]:
            self.cpu.mem_map(address,4096)

    def guard(self,cpu,address,size,user_data):
        if self.mode == 'payment':
            if address == self.CODE+0xb1e4:
                self.rejected = True
                cpu.emu_stop()
                return
            allowed = [(self.CODE+0xb083,self.CODE+0xb0e9)]
        else:
            allowed = [(self.CODE+0xad5f,self.CODE+0xae3a),(self.CODE+0x68a0,self.CODE+0x68bd)]
        if not any(start <= address < end for start,end in allowed):
            raise ValueError(f'Unexpected construction control-flow escape: {address:#x}')

    def payment(self,costs,balances):
        self.mode='payment';self.rejected=False
        template=bytearray(171)
        struct.pack_into('<4I',template,0x7c,*costs)
        self.cpu.mem_write(self.TEMPLATE,bytes(template))
        self.cpu.mem_write(0x500,struct.pack('<I',self.TEMPLATE))
        self.cpu.mem_write(0x508,struct.pack('<I',self.FUNDS-0x18))
        self.cpu.mem_write(self.FUNDS,struct.pack('<4I',*balances))
        self.cpu.mem_write(self.NEIGHBOR,self.SENTINEL)
        self.cpu.reg_write(UC_X86_REG_ESP,self.STACK+0xf00)
        self.cpu.reg_write(UC_X86_REG_EFLAGS,2)
        self.cpu.emu_start(self.CODE+0xb083,self.CODE+0xb0e9,count=1000)
        expected=self.CODE+(0xb1e4 if self.rejected else 0xb0e9)
        if self.cpu.reg_read(UC_X86_REG_EIP) != expected:
            raise ValueError('Payment did not reach an expected boundary')
        if bytes(self.cpu.mem_read(self.NEIGHBOR,12)) != self.SENTINEL:
            raise ValueError('Payment changed another owner balance')
        funds=struct.unpack('<4I',self.cpu.mem_read(self.FUNDS,16))
        return struct.pack('<B4I',not self.rejected,*funds)

    def placement(self,spec,footprint,mask,occupied,x,y):
        self.mode='placement'
        width,height=spec['width'],spec['height']
        template=bytearray(171)
        template[3],template[4]=footprint
        template[0x58:0x60]=bytes(mask)
        occupancy=[0]*(width*height*3)
        for plane,cell,reference in occupied:
            occupancy[plane*width*height+cell]=reference
        self.cpu.mem_write(self.TEMPLATE,bytes(template))
        self.cpu.mem_write(0x6ca+4,struct.pack('<I',self.TEMPLATE))
        self.cpu.mem_write(0x18de,struct.pack('<II',width,height))
        self.cpu.mem_write(0x18ee,struct.pack('<I',self.MAP))
        self.cpu.mem_write(0x18fe,struct.pack('<I',self.PROPERTIES))
        self.cpu.mem_write(0x18ce,struct.pack('<I',self.OCCUPANCY))
        self.cpu.mem_write(0x6aa,struct.pack('<I',self.OBJECT))
        self.cpu.mem_write(self.MAP,struct.pack(f'<{len(spec["cells"])}I',*spec['cells']))
        self.cpu.mem_write(self.PROPERTIES,bytes.fromhex(spec['properties_hex']))
        self.cpu.mem_write(self.OCCUPANCY,struct.pack(f'<{len(occupancy)}I',*occupancy))
        self.cpu.mem_write(self.STACK+0xf00,struct.pack('<I',self.STOP))
        self.cpu.reg_write(UC_X86_REG_EAX,0)  # Template 0 of a synthetic army slot.
        self.cpu.reg_write(UC_X86_REG_ECX,x)
        self.cpu.reg_write(UC_X86_REG_EDX,y)
        self.cpu.reg_write(UC_X86_REG_ESP,self.STACK+0xf00)
        self.cpu.reg_write(UC_X86_REG_EFLAGS,2)
        self.cpu.emu_start(self.CODE+0xad5f,self.STOP,count=10000)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError('Placement did not return')
        flags=self.cpu.reg_read(UC_X86_REG_EFLAGS)
        if flags & 1:
            status,reference=2,(self.cpu.reg_read(UC_X86_REG_EDI)-self.OBJECT)&0xffffffff
        else:
            status,reference=(0 if flags & 0x40 else 1),0
        return struct.pack('<BI',status,reference)


def generate(game):
    analysis,code=analyze(game)
    files={p.name.upper():p for p in game.iterdir() if p.is_file()}
    dat=files['ARMY__02.DAT'].read_bytes()
    costs=[]
    for index in [2,3,4,5]:
        record=dat[2+index*171:2+(index+1)*171]
        costs.append(list(struct.unpack_from('<4I',record,0x7c)))
    costs.append([0xffffffff,0,1,0xffffffff])
    payment={'costs':costs,'balance_values':[0,1,49,50,79,80,149,150,0xffffffff]}
    placement={'width':8,'height':8,
               'cells':[((x+3*y)%8)|0xbeef0000 for y in range(8) for x in range(8)],
               'properties_hex':bytes(v for c in range(8) for v in [0xa0|c,7,8,9,10,11,12,13]).hex(),
               'footprints':[[4,3],[4,2],[1,1],[3,3],[8,8]],
               'masks':[[1]*8,[0]*8,list(dat[2+2*171+0x58:2+2*171+0x60])],
               'occupancy_variants':[[],[[0,18,16]],[[1,18,32]],[[2,18,48]],
                                     [[0,18,16],[1,18,32],[2,18,48]],[[2,17,48],[0,18,16]]],
               'positions':[0,16,32,48,64,96,128,160,192,224,240,256,272,8192,8256,8288,0xffffffff]}
    original=ConstructionOracle(code)
    digest=hashlib.sha256();count=0
    for cost in payment['costs']:
        for balances in product(payment['balance_values'],repeat=4):
            digest.update(original.payment(cost,balances));count+=1
    payment.update({'case_count':count,'sha256':digest.hexdigest()})
    print(f'Original construction payment: {count} cases',flush=True)
    digest=hashlib.sha256();count=0
    for footprint,mask,occupied,x,y in product(*(placement[key] for key in ['footprints','masks','occupancy_variants','positions','positions'])):
        digest.update(original.placement(placement,footprint,mask,occupied,x,y));count+=1
    placement.update({'case_count':count,'sha256':digest.hexdigest()})
    print(f'Original placement: {count} cases',flush=True)
    return {'format_version':1,'source_sha256':{name:analysis['source_sha256'][name] for name in ['NORWIND.EXE','ARMY__02.DAT']},
            'payment':payment,'placement':placement,
            'output_layout':{'payment':'accepted:u8,balances:4*u32le','placement':'status:u8,blocking_object_offset:u32le'},
            'restrictions':['Payment prefix only; not the full transaction and not a guarantee of later placement or allocation.',
                            'Placement executes the complete AD5F function and real 68A0 lookup without stubs.',
                            'Map and footprints satisfy 1..255 and footprint <= map; coordinates retain original low-byte wrap.',
                            'Synthetic terrain/occupancy fixtures; map-specific scripted and ownership checks outside AD5F are excluded.']}


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
