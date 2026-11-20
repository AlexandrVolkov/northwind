#!/usr/bin/env python3
"""Verify the pressed-frame branch of original menu plaque callback 18F16.

Only the indirect action is replaced by a one-byte counter/RET sentinel.
The native callback and frame helper execute normally until that boundary.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EDI
from analyze_executable import INNER_MZ, analyze, read_le_objects
from verify_texts import TextsOracle


class PlaqueOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at=address-self.CODE
        if not (0x18ED4 <= at < 0x18EF5 or 0x18F16 <= at < 0x19063 or at == 0x19279):
            raise ValueError(f'Unexpected plaque escape: {at:X}')


def cases():
    for index in range(3):
        for tick,last in ((0,0),(2,0),(3,0),(5,0),(0,254),(255,250)):
            for offset in (0,1,2,3):
                yield index,tick,last,offset


def generate(game):
    _,code=analyze(game); o=PlaqueOracle(code); c=o.cpu
    # INC byte ptr DS:[9000h]; RET. Native handler calls this only on frame completion.
    c.mem_write(o.CODE+0x19279,b'\xfe\x05\x00\x90\x00\x00\xc3')
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    data=objects[2]['payload']; widget=o.FRAME+0x2000
    digest=hashlib.sha256();count=0
    for index,tick,last,offset in cases():
        source=0xADBC+49*index
        record=bytearray(data[source:source+49]);base=struct.unpack_from('<I',record,0x24)[0]
        struct.pack_into('<I',record,0x0A,o.CODE+0x19279)
        c.mem_write(widget,bytes(record));c.mem_write(widget+0x12,b'\x01')
        c.mem_write(widget+0x13,bytes([last]));c.mem_write(widget+0x28,struct.pack('<I',base+offset))
        c.mem_write(0xA0AA,b'\x00');c.mem_write(0x19E4,bytes([tick]));c.mem_write(0x9000,b'\x00')
        c.reg_write(UC_X86_REG_EDI,widget);c.reg_write(UC_X86_REG_EAX,1)
        o.run('plaque',0x18F16)
        outlast=c.mem_read(widget+0x13,1)[0]
        outframe=struct.unpack('<I',c.mem_read(widget+0x28,4))[0]
        action=c.mem_read(0x9000,1)[0]
        digest.update(struct.pack('<BIB',outlast,outframe,action));count+=1
    return dict(format_version=1,source_sha256=hashlib.sha256((game/'NORWIND.EXE').read_bytes()).hexdigest(),
                cases=count,output_sha256=digest.hexdigest(),
                stubbed_calls=['Action pointer at widget+0A replaced by counter/RET'])


if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game',type=Path,required=True)
    ap.add_argument('--fixture',type=Path,default=Path('tests/fixtures/menu-button-press.json'))
    a=ap.parse_args();r=generate(a.game);a.fixture.write_text(json.dumps(r,indent=2)+'\n');print(r)
