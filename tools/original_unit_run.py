"""One unit of the live snapshot under the original unit script (D182, AH=1) ordered to a far cell: usage original_unit_run.py GOALX GOALY TICKS 0xd182 STATE14 SNAPSHOT"""
import sys,struct
sys.path.insert(0,'/home/user/northwind/tools')
import original_engine as E
S=sys.argv[6] if len(sys.argv)>6 else 'snap72.bin'

e=E.Engine(S)
rec=e.pool+204
print('code bytes match', bytes(e.cpu.mem_read(E.CODE+0x9210,8)).hex())
tbl=struct.unpack('<I',e.dsr(0x6ce+0x56*2,4))[0]
e.put(rec+4,struct.pack('<II',21*32+16,29*32+16)); goal=(int(sys.argv[1]),int(sys.argv[2]))
e.put(rec+0x22,bytes(goal)); e.put(rec+0x2d,bytes([0x22])); e.put(rec+0x14,bytes([int(sys.argv[5]) if len(sys.argv)>5 else 1])); e.put(rec+0x15,bytes([0])); e.dsw(0x1c49,struct.pack('<I',tbl)); e.dsw(0x1c61,bytes([200]))
for t in range(int(sys.argv[3])):
    try: e.call(int(sys.argv[4],16) if len(sys.argv)>4 else 0x9210,esi=rec,eax=0x100)
    except Exception as ex:
        print('stop',t,ex,hex(e.cpu.reg_read(E.UC_X86_REG_EIP))); break
    x,y=struct.unpack('<II',bytes(e.cpu.mem_read(rec+4,8)))
    if t%30==0: print(t,(x>>5,y>>5),hex(e.cpu.mem_read(rec+0x2d,1)[0]))
