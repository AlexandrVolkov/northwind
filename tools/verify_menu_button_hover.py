#!/usr/bin/env python3
"""Execute original AL=1 plaque hover branch with a controlled hit-test result.

18699 is replaced at entry by a CMP/RET stub reading the case flag.
No real button action is expected in these one-tick cases; a counter/RET
at widget+0A catches any unexpected action.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EDI
from analyze_executable import INNER_MZ, analyze, read_le_objects
from verify_texts import TextsOracle


class HoverOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at=address-self.CODE
        if not (0x18ED4 <= at < 0x18F16 or 0x18F16 <= at < 0x19063 or at in (0x18699,0x19279)):
            raise ValueError(f'Unexpected plaque hover escape: {at:X}')


def cases():
    for index in (0,1,2,6):
        for inside in (False,True):
            for mouse in (False,True):
                for key in (False,True):
                    for tick,last in ((0,0),(3,0),(0,254)):
                        for offset in (0,1,2,3):
                            yield index,inside,mouse,key,tick,last,offset


def generate(game):
    _,code=analyze(game);o=HoverOracle(code);c=o.cpu
    c.mem_write(o.CODE+0x19279,b'\xfe\x05\x00\x90\x00\x00\xc3')
    # CMP byte [9001h],1; RET: inside=1 clears CF, outside=0 sets CF.
    c.mem_write(o.CODE+0x18699,b'\x80\x3d\x01\x90\x00\x00\x01\xc3')
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    data=objects[2]['payload'];widget=o.FRAME+0x2000
    digest=hashlib.sha256();count=0
    for index,inside,mouse,key,tick,last,offset in cases():
        c.mem_write(0x9001,b'\x01' if inside else b'\x00')
        source=0xAED6 if index==6 else 0xADBC+49*index
        record=bytearray(data[source:source+49])
        base=struct.unpack_from('<I',record,0x24)[0]
        struct.pack_into('<I',record,0x0A,o.CODE+0x19279)
        struct.pack_into('<H',record,0x0E,0x1234)
        c.mem_write(widget,bytes(record));c.mem_write(widget+0x12,b'\x00')
        c.mem_write(widget+0x13,bytes([last]));c.mem_write(widget+0x28,struct.pack('<I',base+offset))
        c.mem_write(0xA0AA,b'\x02' if index==6 else b'\x00');c.mem_write(0x19E4,bytes([tick]));c.mem_write(0x9000,b'\x00')
        c.mem_write(0x194B,b'\x01' if mouse else b'\x00')
        c.mem_write(0x1C35,struct.pack('<H',0x1234 if key else 0x4321))
        c.reg_write(UC_X86_REG_EDI,widget);c.reg_write(UC_X86_REG_EAX,1)
        o.run('hover',0x18F16)
        previous=c.mem_read(widget+0x13,1)[0]
        frame=struct.unpack('<I',c.mem_read(widget+0x28,4))[0]
        state=c.mem_read(widget+0x12,1)[0]
        action=c.mem_read(0x9000,1)[0]
        exp_frame=base+offset; exp_last=last; exp_state=0
        if (inside and mouse) or key:
            exp_state=1
        elif ((tick-last)&255)>=3:
            exp_last=tick
            if inside:
                exp_frame=base+1 if exp_frame+1>=base+2 else exp_frame+1
            elif exp_frame<=base+1:
                exp_frame=base if exp_frame+1>=base+1 else exp_frame+1
            else:
                exp_frame=base+1 if exp_frame-1<=base+1 else exp_frame-1
        if (frame,previous,state,action)!=(exp_frame,exp_last,exp_state,0):
            raise ValueError(f'Mismatch {index,inside,mouse,key,tick,last,offset}: native {(frame,previous,state,action)} expected {(exp_frame,exp_last,exp_state,0)}')
        digest.update(struct.pack('<BIBB',previous,frame,state,action));count+=1
    return dict(format_version=1,source_sha256=hashlib.sha256((game/'NORWIND.EXE').read_bytes()).hexdigest(),
                cases=count,output_sha256=digest.hexdigest(),
                stubbed_calls=['18699 hit-test replaced with CMP flag plus RET','widget action replaced with counter/RET'])


if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game',type=Path,required=True)
    ap.add_argument('--fixture',type=Path,default=Path('tests/fixtures/menu-button-hover.json'))
    a=ap.parse_args();r=generate(a.game);a.fixture.write_text(json.dumps(r,indent=2)+'\n');print(r)
