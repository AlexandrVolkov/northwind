#!/usr/bin/env python3
"""Export REA/Ghidra observed function assembly into sidecar evidence and an assembly SQLite DB."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess


class Mcp:
    def __init__(self, executable, env, log):
        self.log=log; self.next_id=0
        self.process=subprocess.Popen([executable,'--mcp'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=log,text=True,env=env)
        self.rpc('initialize',dict(protocolVersion='2024-11-05',capabilities={},clientInfo=dict(name='northwind-assembly-index',version='1')))
        self.process.stdin.write(json.dumps(dict(jsonrpc='2.0',method='notifications/initialized'))+'\n');self.process.stdin.flush()
    def rpc(self, method, params):
        self.next_id+=1; self.process.stdin.write(json.dumps(dict(jsonrpc='2.0',id=self.next_id,method=method,params=params))+'\n');self.process.stdin.flush()
        while True:
            line=self.process.stdout.readline()
            if not line:raise RuntimeError(f'REA MCP exited: {self.process.poll()}')
            reply=json.loads(line)
            if reply.get('id')==self.next_id:
                if 'error'in reply:raise RuntimeError(str(reply['error']))
                return reply['result']
    def tool(self,name,arguments):return self.rpc('tools/call',dict(name=name,arguments=arguments))
    def close(self):
        self.process.stdin.close()
        try:self.process.wait(timeout=30)
        except subprocess.TimeoutExpired:self.process.terminate();self.process.wait(timeout=30)


def evidence(reply):
    if isinstance(reply.get('structuredContent'),dict):return reply['structuredContent']
    for item in reply.get('content',[]):
        if item.get('type')=='text':
            try:return json.loads(item['text'])
            except json.JSONDecodeError:pass
    return reply


def export(db_path, output, executable):
    if output.exists():raise ValueError('Choose a new evidence directory')
    output.mkdir(parents=True)
    (output/'tool.json').write_text(json.dumps(dict(rea_version=subprocess.check_output([executable,'--version'],text=True).strip())))
    db=sqlite3.connect(db_path.resolve().as_uri()+'?mode=rw',uri=True)
    db.execute('CREATE TABLE IF NOT EXISTS rea_observations(module TEXT,operation TEXT,selector TEXT,evidence_id TEXT,is_error INTEGER,payload TEXT)')
    modules=db.execute('SELECT id,sha256 FROM modules WHERE is_code=1 ORDER BY id').fetchall();reports=[]
    for module,digest in modules:
        label=module.replace(':','_');target=(db_path.parent/(label+'.bin')).resolve()
        if hashlib.sha256(target.read_bytes()).hexdigest()!=digest:raise ValueError('Module digest mismatch')
        roots={r[0]for r in db.execute('SELECT start FROM annotations WHERE module=? AND classification=?',(module,'code'))}
        # Roots describe known entry/candidate addresses, not claims of recovered author functions.
        if module=='LE:1':
            profile=json.loads((Path(__file__).resolve().parents[1]/'docs/code-analysis.json').read_text())
            roots.update(r['target_offset']for r in profile['fs_assignments']);roots.add(0x1cc)
        else:
            profile=json.loads((Path(__file__).resolve().parents[1]/'docs/code-analysis.json').read_text())
            army=next(a for a in profile['armies']if a['wrk']==module);roots.update(c['wrk_offset']for c in army['callbacks'])
        seed=(output/(label+'.seeds.tsv')).resolve()
        seed.write_text(''.join(f'0x{at:x}\tfunction\tcandidate_{at:x}\n'for at in sorted(roots)),encoding='utf-8')
        env=dict(os.environ);env['REA_GHIDRA_SEED_FILE']=str(seed)
        env.pop('REA_GHIDRA_CHECKPOINT_DIR',None);env.pop('REA_GHIDRA_PROJECT_PATH',None)
        log=(output/(label+'.stderr.log')).open('w');client=Mcp(executable,env,log)
        count=errors=0;inventory=[]
        with (output/(label+'.observations.jsonl')).open('w')as stream:
            def observe(name,arguments):
                nonlocal errors
                reply=client.tool(name,arguments);raw=evidence(reply);bad=bool(reply.get('isError'))
                if bad:errors+=1
                row=dict(module=module,operation=name,arguments=arguments,is_error=bad,evidence=raw)
                stream.write(json.dumps(row,ensure_ascii=False)+'\n');stream.flush()
                db.execute('INSERT INTO rea_observations VALUES(?,?,?,?,?,?)',(module,name,json.dumps(arguments),raw.get('evidence_id'),bad,json.dumps(raw,ensure_ascii=False)))
                return raw,bad
            try:
                opened,bad=observe('open_binary',dict(path=str(target),format='raw-binary',architecture='x86',entry_point='0x0',provider_id='ghidra'))
                if not bad:
                    listed,bad=observe('list_procedures',{})
                    if not bad:
                        value=listed.get('normalized_result',{})
                        (output/(label+'.inventory.json')).write_text(json.dumps(listed,ensure_ascii=False,indent=2))
                        # Provider inventories are records with a value selector. Fail visibly on unknown schema.
                        if isinstance(value,dict) and 'procedures' not in value:raise ValueError('Unrecognized REA inventory schema')
                        inventory=value['procedures'] if isinstance(value,dict) else value
                        if not isinstance(inventory,list):raise ValueError(f'Unrecognized REA inventory: {module}')
                        for proc in inventory:
                            selector=proc.get('address') or proc.get('value') or proc.get('name')
                            if not selector:raise ValueError(f'Missing procedure selector: {proc}')
                            _,bad=observe('read_function_instructions',dict(procedure=str(selector)))
                            count+=not bad
                observe('close_binary',{})
                observe('reset_investigation',dict(save_path=str((output/(label+'.investigation.json')).resolve())))
            finally:client.close();log.close();db.commit()
        reports.append(dict(module=module,sha256=digest,observed_procedures=len(inventory),assembly_queries_succeeded=count,errors=errors))
        print(json.dumps(reports[-1]),flush=True)
    db.close();(output/'summary.json').write_text(json.dumps(dict(format_version=1,provider='REA/Ghidra',modules=reports,
        limitations=['Seeded function boundaries are analysis candidates, not restored author symbols.',
                     'Each module is mapped separately as raw x86/32; no complete LE relocation or segmented DS/FS runtime model.',
                     'Provider errors and missing instructions remain explicit; inventory counts do not prove discovery completeness.']),indent=2)+'\n')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--db',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--rea',required=True);a=p.parse_args();export(a.db,a.output,a.rea)
