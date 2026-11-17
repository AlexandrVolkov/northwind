#!/usr/bin/env python3
"""Run bounded lumbermill WRK operations and real EXE owner/statistics helpers."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EDX, UC_X86_REG_EIP
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class LumbermillOracle(OccupancyOracle):
    def guard(self,cpu,address,size,user_data):
        if self.mode=='process' and address==self.WRK+0x412:self.finished=True;cpu.emu_stop();return
        if self.mode=='receive' and address==self.WRK+0x433:self.accepted=True
        ranges=[(self.WRK+0x3e4,self.WRK+0x412),(self.CODE+0x6877,self.CODE+0x68a0),
                (self.CODE+0x1ead8,self.CODE+0x1eaed)] if self.mode=='process' else [(self.WRK+0x41c,self.WRK+0x44e)]
        if not any(lo<=address<hi for lo,hi in ranges):raise ValueError(f'Unexpected lumbermill escape: {address:#x}')


def mill_record(queue,countdown,owner=0,flags=0):
    raw=bytearray((i*11+7)&255 for i in range(37));raw[0]=2;raw[3]=owner;raw[0xe]=4
    raw[0x23]=countdown;raw[0x24]=queue;struct.pack_into('<H',raw,0x17,flags)
    return raw


def worker_record(cargo,flags):
    raw=bytearray((i*7+9)&255 for i in range(54));raw[0x31]=cargo;struct.pack_into('<H',raw,0x17,flags)
    return raw


def generate(game):
    report,code=analyze(game);oracle=LumbermillOracle(code,report['fs_assignments'])
    wrk=(game/'ARMY__02.WRK').read_bytes();oracle.cpu.mem_write(oracle.WRK,wrk)
    dat=bytearray(2+6*171);struct.pack_into('<H',dat,0,6*171)
    oracle.cpu.mem_write(oracle.DAT,bytes(dat));oracle.cpu.mem_write(0x6ca+2*0x56+4,struct.pack('<I',oracle.DAT+2))
    template=oracle.DAT+2+4*171
    fixture={'format_version':1,'source_sha256':{k:report['source_sha256'][k] for k in ['NORWIND.EXE','ARMY__02.WRK']},
             'process':{'queues':[0,1,2,127,255],'countdowns':list(range(256)),'resets':[0,1,30,100,255],
                        'stocks':[0,5,4294967285,4294967295],'statistics':[0,4294967288],'owners':[0,3]},
             'receive':{'mill_flags':[0,64,65535,512],'worker_flags':[0,1,64,512,65535],
                        'cargo':list(range(256)),'template_flags':[0,131072,4096,4294967295],'queues':[0,1,254,255]}}
    digest=hashlib.sha256();count=0;oracle.mode='process'
    for queue,timer,reset,stock,stats,owner in product(*(fixture['process'][k] for k in ['queues','countdowns','resets','stocks','statistics','owners'])):
        raw=mill_record(queue,timer,owner)
        oracle.cpu.mem_write(oracle.POOL+1,bytes(raw));oracle.cpu.reg_write(UC_X86_REG_ESI,oracle.POOL+1)
        oracle.cpu.reg_write(UC_X86_REG_EDI,template);oracle.cpu.mem_write(template+0x79,bytes([reset]))
        owner_address=0x97a+owner*0xf5;stat_address=oracle.PARTICIPANTS+owner*0x1a35+0x1a10
        oracle.cpu.mem_write(owner_address+0x1c,struct.pack('<I',stock));oracle.cpu.mem_write(stat_address,struct.pack('<I',stats))
        oracle.finished=False;oracle.start(oracle.WRK+0x3e4)
        if not oracle.finished:raise ValueError('Lumbermill processing failed to reach boundary')
        digest.update(bytes(oracle.cpu.mem_read(oracle.POOL+1,37)))
        digest.update(bytes(oracle.cpu.mem_read(owner_address+0x1c,4))+bytes(oracle.cpu.mem_read(stat_address,4)));count+=1
    fixture['process'].update({'case_count':count,'sha256':digest.hexdigest()});print('Processing',count,flush=True)
    digest=hashlib.sha256();count=0;oracle.mode='receive'
    for mill_flags,worker_flags,cargo,flags,queue in product(*(fixture['receive'][k] for k in ['mill_flags','worker_flags','cargo','template_flags','queues'])):
        oracle.cpu.mem_write(oracle.POOL+1,bytes(mill_record(queue,100,0,mill_flags)))
        oracle.cpu.mem_write(oracle.POOL+1024,bytes(worker_record(cargo,worker_flags)))
        oracle.cpu.reg_write(UC_X86_REG_ESI,oracle.POOL+1);oracle.cpu.reg_write(UC_X86_REG_EDI,oracle.POOL+1024)
        oracle.cpu.mem_write(template+0x40,struct.pack('<I',flags));oracle.cpu.reg_write(UC_X86_REG_EDX,template)
        oracle.accepted=False;oracle.start(oracle.WRK+0x41c)
        if oracle.cpu.reg_read(UC_X86_REG_EIP)!=oracle.STOP:raise ValueError('Lumbermill handoff did not return')
        digest.update(bytes(oracle.cpu.mem_read(oracle.POOL+1,37))+bytes(oracle.cpu.mem_read(oracle.POOL+1024,54))+bytes([oracle.accepted]));count+=1
    fixture['receive'].update({'case_count':count,'sha256':digest.hexdigest()});print('Handoff',count,flush=True)
    return fixture


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,required=True);args=parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game),indent=2)+'\n',encoding='utf-8')
