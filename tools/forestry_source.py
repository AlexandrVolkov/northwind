"""Extract data constants only after verifying their LE fixup target objects."""
import hashlib
import struct
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects


def source_constants(game):
    data=(game/'NORWIND.EXE').read_bytes()
    if hashlib.sha256(data).hexdigest()!=EXE_SHA256:raise ValueError('Unknown EXE version')
    le,objects=read_le_objects(data,INNER_MZ)
    u32=lambda at:struct.unpack_from('<I',data,at)[0]
    pages=le+u32(le+0x68);records=le+u32(le+0x6c)
    evidence=[]
    for source,target_object,target_offset in [(0xcdc2,3,0x7c0),(0x9e4a,2,0x297e)]:
        page=source//4096
        blob=data[records+u32(pages+page*4):records+u32(pages+(page+1)*4)]
        # These specific records patch 32-bit operands, with 16-bit page and target offsets.
        expected=struct.pack('<BBHBH',7,0,source%4096,target_object,target_offset)
        if blob.count(expected)!=1:raise ValueError('Unexpected LE fixup record')
        evidence.append({'source_operand':source,'target_object':target_object,'target_offset':target_offset})
    return {'format_version':1,'source_sha256':EXE_SHA256,'fixups':evidence,
            'patch_masks':list(struct.unpack_from('<9I',objects[2]['payload'],0x7c0)),
            'random_values':list(objects[1]['payload'][0x297e:0x2a7e])}
