#!/usr/bin/env python3
"""Execute the native crystal-orb action through its external call boundary."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EDI
from analyze_executable import INNER_MZ, analyze, read_le_objects
from verify_texts import TextsOracle


EXTERNAL = (0x19294, 0x1CC4E, 0x1CC71)


class OrbOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not (0x19436 <= at < 0x19465 or at in EXTERNAL):
            raise ValueError(f'Unexpected orb escape: {at:X}')


def generate(game):
    _,code=analyze(game);o=OrbOracle(code);c=o.cpu
    for i,entry in enumerate(EXTERNAL):
        # MOV byte ptr DS:[9000h+i],1; RET; proves each outward call was reached.
        c.mem_write(o.CODE+entry,b'\xc6\x05'+struct.pack('<I',0x9000+i)+b'\x01\xc3')
    _,objects=read_le_objects((game/'NORWIND.EXE').read_bytes(),INNER_MZ)
    record=objects[2]['payload'][0xAED6:0xAED6+49]
    widget=o.FRAME+0x2000
    c.mem_write(widget,record);c.mem_write(widget+0x12,b'\x01')
    c.mem_write(widget+0x28,struct.pack('<I',266))
    c.mem_write(0xA0AA,b'\x02');c.mem_write(0x9000,b'\0\0\0')
    c.reg_write(UC_X86_REG_EDI,widget);o.run('orb',0x19436)
    result=dict(format_version=1,source_sha256=hashlib.sha256((game/'NORWIND.EXE').read_bytes()).hexdigest(),
                action_le1=0x19436,widget_le3=0xAED6,
                pressed_latch=c.mem_read(widget+0x12,1)[0],
                frame=struct.unpack('<I',c.mem_read(widget+0x28,4))[0],
                phase=c.mem_read(0xA0AA,1)[0],external_calls=list(c.mem_read(0x9000,3)))
    if (result['pressed_latch'],result['frame'],result['phase'],result['external_calls'])!=(0,264,3,[1,1,1]):
        raise ValueError(f'Unexpected native orb action: {result}')
    return result


if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game',type=Path,required=True)
    ap.add_argument('--fixture',type=Path,default=Path('tests/fixtures/menu-orb.json'))
    a=ap.parse_args();r=generate(a.game);a.fixture.write_text(json.dumps(r,indent=2)+'\n');print(r)
