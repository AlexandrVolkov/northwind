#!/usr/bin/env python3
"""Execute original population recount and free-population growth, exporting parameters and result hashes."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_ECX, UC_X86_REG_EIP
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class PopulationOracle(OccupancyOracle):
    OWNER = 0x97a

    def __init__(self, code, assignments):
        super().__init__(code, assignments)
        self.cpu.mem_write(0x6c6, b'\x01')
        self.cpu.mem_write(0x6aa, struct.pack('<I', self.POOL))
        self.cpu.mem_write(0x6ce + 2 * 0x56, struct.pack('<I', self.DAT))

    def guard(self, cpu, address, size, user_data):
        ranges = [(0xd453, 0xd5b1), (0x68bd, 0x68d8), (0x219c3, 0x21a00)] if self.mode == 'recount' else [(0xd40c, 0xd453)]
        if not any(self.CODE + start <= address < self.CODE + end for start, end in ranges):
            raise ValueError(f'Unexpected population {self.mode} control-flow escape: {address:#x}')

    def execute(self, address):
        self.cpu.reg_write(UC_X86_REG_ECX, 0)
        self.start(self.CODE + address)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError(f'Population {self.mode} exceeded its instruction budget')

    def recount(self, case):
        self.mode = 'recount'
        self.cpu.mem_write(self.OWNER, b'\xa5' * 0xf5)
        self.cpu.mem_write(0x18de, struct.pack('<II', case['width'], case['height']))
        objects = case['objects']
        self.cpu.mem_write(self.OWNER + 9, struct.pack('<I', 1 + len(objects) * 49))
        self.cpu.mem_write(self.OWNER + 0x0e, struct.pack('<I', 1))
        for index, obj in enumerate(objects):
            raw = bytearray(49); raw[0] = 2; raw[0x0e] = index; raw[0x19] = obj['condition']
            struct.pack_into('<II', raw, 4, obj['x'], obj['y']); struct.pack_into('<I', raw, 0x10, 49)
            dat = bytearray(171); dat[0x46] = obj['threshold']
            struct.pack_into('<I', dat, 0x40, obj['flags']); struct.pack_into('<I', dat, 0x78, obj['support']); struct.pack_into('<I', dat, 0x88, obj['demand'])
            self.cpu.mem_write(self.POOL + 1 + index * 49, bytes(raw))
            self.cpu.mem_write(self.DAT + index * 171, bytes(dat))
        self.execute(0xd453)
        return bytes(self.cpu.mem_read(self.OWNER + 0x28, 12)) + bytes(self.cpu.mem_read(self.OWNER + 0xc9, 4))

    def growth(self, case):
        self.mode = 'growth'
        self.cpu.mem_write(self.OWNER, b'\xa5' * 0xf5)
        self.cpu.mem_write(self.OWNER + 0x24, struct.pack('<4I', *(case[k] for k in ['free', 'used', 'support', 'limit'])))
        self.cpu.mem_write(0x1ca9, struct.pack('<I', case['tick']))
        self.execute(0xd40c)
        return bytes(self.cpu.mem_read(self.OWNER + 0x24, 4))


def recount_cases():
    result = []
    def obj(condition=70, threshold=10, flags=0x108, demand=1, support=10, x=64, y=96):
        return dict(condition=condition, threshold=threshold, flags=flags, demand=demand, support=support, x=x, y=y)
    def add(objects, width=128, height=128):
        result.append(dict(width=width, height=height, objects=objects))
    add([])
    # Signed condition/threshold boundaries; a negative threshold is intentional.
    for condition in [0,9,10,11,70,127,128,255]:
        for threshold in [10,128,255]:add([obj(condition=condition, threshold=threshold)])
    # Independent farm-support and territorial-building masks, with irrelevant bits mixed in.
    for flags in [0,8,0x20,0x28,0x40,0x48,0x68,0x100,0x108,0x118,0x158,0x208,0xf08,0xffffffff]:
        add([obj(flags=flags)])
    for width,height in [(1,1),(8,6),(128,128),(255,255)]:
        corners = [(0,0),(width*32-1,0),(0,height*32-1),(width*32-1,height*32-1)]
        for x,y in corners:add([obj(x=x,y=y)],width,height)
        add([obj(x=x,y=y,flags=8,support=0) for x,y in corners],width,height)
    # Empty support can coexist with active demand, or support can lack an eligible bounds contributor.
    add([obj(flags=0,demand=5,support=50)])
    add([obj(flags=0x158,demand=0,support=10),obj(flags=0,demand=7,support=100)])
    add([obj(flags=0x118,demand=0,support=10),obj(flags=0x158,demand=0,support=10)])
    # Full-width positions exercise the original u32 arithmetic and byte coordinate projection.
    for x,y in [(0xffffffff,0xffffffff),(0x80000000,0),(0,0x80000000),(0xffffffff,0),(0,0xffffffff)]:
        add([obj(x=x,y=y)])
    for value in [0,1,0x7fffffff,0xfffffffe,0xffffffff]:
        add([obj(demand=value,support=value),obj(demand=value,support=value,x=320,y=640)])
    # Stable generated input corpus. These are test parameters, not a Python implementation of the rules.
    random = 0x6f726461
    def next_value():
        nonlocal random
        random = (1664525 * random + 1013904223) & 0xffffffff
        return random
    conditions = [0,10,11,70,127,128,255]
    flags = [0,8,0x108,0x118,0x158,0x208,0x28,0x48,0x68,0xffffffff]
    quantities = [0,1,5,10,30,100,0x7fffffff,0xffffffff]
    for index in range(48):
        width,height = [(16,12),(32,32),(128,128),(255,255)][index % 4]
        objects=[]
        for _ in range(next_value() % 9):
            objects.append(obj(condition=conditions[next_value()%len(conditions)], threshold=[10,70,128,255][next_value()%4],
                flags=flags[next_value()%len(flags)], demand=quantities[next_value()%len(quantities)],support=quantities[next_value()%len(quantities)],
                x=next_value()%(width*32),y=next_value()%(height*32)))
        add(objects,width,height)
    return result


def growth_cases():
    result=[]
    # Tick values straddle every mask used by support bands, including higher ignored bits.
    ticks=[0,1,2,126,127,128,254,255,256,510,511,512,1022,1023,1024,0x80000000,0xffffffff]
    profiles=[(0,0,0),(0,0,1),(0,0,10),(3,6,10),(4,6,10),(5,6,10),(100,0,10),
              (0xffffffff,0,0xffffffff),(0xffffffff,1,1),(0xfffffffe,3,2),(0,0xffffffff,0xffffffff)]
    for support in [0,1,31,32,63,64,95,96,100,101,0xffffffff]:
        for tick in ticks:
            for free,used,limit in profiles:
                result.append(dict(tick=tick,free=free,used=used,support=support,limit=limit))
    return result


def generate(game):
    report,code=analyze(game);oracle=PopulationOracle(code,report['fs_assignments'])
    result=dict(format_version=1,source_sha256=report['source_sha256']['NORWIND.EXE'])
    for name, cases, execute in [('recount',recount_cases(),oracle.recount),('growth',growth_cases(),oracle.growth)]:
        digest=hashlib.sha256()
        for case in cases:digest.update(execute(case))
        result[name]=dict(cases=cases,case_count=len(cases),sha256=digest.hexdigest())
        print(name,len(cases),digest.hexdigest(),flush=True)
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game',type=Path,required=True);parser.add_argument('--fixture',type=Path,required=True)
    args=parser.parse_args();args.fixture.write_text(json.dumps(generate(args.game),indent=2)+'\n',encoding='utf-8')
