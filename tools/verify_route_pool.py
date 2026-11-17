#!/usr/bin/env python3
"""Execute isolated original route packing, nibble reads and release; export only hashes."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class RouteOracle(OccupancyOracle):
    ROUTES, SOURCE = 0x1000000, 0x1100000
    def __init__(self, code, assignments):
        super().__init__(code, assignments)
        self.cpu.mem_map(self.ROUTES, 32*8192)
        self.cpu.mem_map(self.SOURCE, 4096)
        self.cpu.mem_write(0x6b6, struct.pack('<I', self.ROUTES))
        self.cpu.mem_write(0x2730, struct.pack('<I', self.SOURCE))
        self.boundary = None

    def guard(self, cpu, address, size, user_data):
        stops = {'allocate': [0x7fa7], 'read': [0x80d2, 0x7ff0], 'release': []}[self.mode]
        if address-self.CODE in stops:
            self.boundary = address-self.CODE; cpu.emu_stop(); return
        allowed = {'allocate': [(0x802c,0x80a5)], 'read': [(0x7fa7,0x7ff0),(0x8120,0x813e)],
                   'release': [(0x8120,0x813e)]}[self.mode]
        if not any(self.CODE+a <= address < self.CODE+b for a,b in allowed):
            raise ValueError(f'Unexpected route execution: {address:#x}')

    def run(self, mode, address, record):
        self.mode = mode; self.boundary = None
        self.cpu.mem_write(self.POOL+1, bytes(record))
        self.cpu.reg_write(UC_X86_REG_ESI, self.POOL+1)
        self.start(self.CODE+address)
        if self.boundary is None and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError('Route instruction budget exhausted')
        return bytes(self.cpu.mem_read(self.POOL+1, len(record)))


def generate(game):
    report, code = analyze(game); o = RouteOracle(code,report['fs_assignments'])
    result = {'format_version': 1, 'source_sha256': report['source_sha256']['NORWIND.EXE'],
              'seeds': [0,7,255], 'owners': [0,1,3,31], 'occupied': [0,1,127,128],
              'lengths': [0,1,2,3,124,125,126,127,128,255], 'results': {}}
    def save(name, digest, count):
        result['results'][name] = {'case_count':count, 'sha256':digest.hexdigest()}
        print(name,count,flush=True)
    digest = hashlib.sha256(); count = 0
    for seed in result['seeds']:
        pattern = bytes((i*37+seed)&255 for i in range(8192))
        for owner in result['owners']:
            for occupied in result['occupied']:
                for length in result['lengths']:
                    pool = bytearray(pattern)
                    for block in range(128): pool[block*64] = 0 if block < occupied else 255
                    o.cpu.mem_write(o.ROUTES+owner*8192,bytes(pool))
                    source = bytes((i*3+seed)&7 for i in range(length))+bytes([8])+bytes(4)
                    o.cpu.mem_write(o.SOURCE,source)
                    record = bytearray(49); record[3] = owner; record[0x25] = seed
                    struct.pack_into('<I',record,0x28,0xffffffff)
                    after = o.run('allocate',0x802c,record)
                    allocated = o.boundary == 0x7fa7
                    if not allocated and (o.cpu.reg_read(UC_X86_REG_EAX)>>8)&255 != 2:
                        raise ValueError('Exhausted pool did not request retry')
                    digest.update(bytes([allocated])+after[0x28:0x2c]+bytes(o.cpu.mem_read(o.ROUTES+owner*8192,8192)))
                    count += 1
    save('allocation',digest,count)
    # Decode every byte in both cursor phases, including end-marker release and target mismatch.
    digest = hashlib.sha256(); count = 0
    for owner in result['owners']:
        for offset in [0,1,62,63]:
            for value in range(256):
                for high in [0,1]:
                    for reached in [0,1]:
                        pool = bytearray(8192); pool[64] = 2; pool[64+offset] = value
                        o.cpu.mem_write(o.ROUTES+owner*8192,bytes(pool))
                        record = bytearray(49); record[3] = owner
                        struct.pack_into('<II',record,4,3*32+16,4*32+16)
                        struct.pack_into('<I',record,0x28,(owner*8192+64+offset)*2+high)
                        o.cpu.mem_write(0x525,bytes([3 if reached else 5,4]))
                        after = o.run('read',0x7fa7,record)
                        # Status: next direction / rebuild / reached target.
                        status = 0 if o.boundary == 0x80d2 else 1 if o.boundary == 0x7ff0 else 2
                        nibble = o.cpu.reg_read(UC_X86_REG_EAX)&255 if status == 0 else 255
                        digest.update(bytes([status,nibble])+after[0x28:0x2c]+bytes(o.cpu.mem_read(o.ROUTES+owner*8192+64,64)))
                        count += 1
    save('read_release',digest,count)
    # Release from every possible nibble in an owner's arena, plus the absent cursor.
    digest = hashlib.sha256(); count = 0
    for owner in result['owners']:
        for cursor in [0xffffffff]+[owner*16384+i for i in range(16384)]:
            pool = bytes((i*37+7)&255 for i in range(8192))
            o.cpu.mem_write(o.ROUTES+owner*8192,pool)
            record = bytearray(49); record[3] = owner; struct.pack_into('<I',record,0x28,cursor)
            after = o.run('release',0x8120,record)
            digest.update(after[0x28:0x2c]+bytes(o.cpu.mem_read(o.ROUTES+owner*8192,8192))); count += 1
    save('release',digest,count)
    return result


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',required=True,type=Path);p.add_argument('--fixture',required=True,type=Path);a=p.parse_args()
    a.fixture.write_text(json.dumps(generate(a.game),indent=2)+'\n',encoding='utf-8')
