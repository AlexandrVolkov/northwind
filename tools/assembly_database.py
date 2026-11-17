#!/usr/bin/env python3
"""Build an object-relative assembly/coverage database. Original bytes stay in the local output."""
import argparse
from collections import Counter, deque
import hashlib
import json
from pathlib import Path
import sqlite3
import struct
from analyze_executable import analyze, read_le_objects, INNER_MZ

ROOT = Path(__file__).resolve().parents[1]
STATUSES = ('unreviewed', 'partial', 'verified', 'ported')


def partition(size, regions):
    """Account for every byte once; overlaps retain all claims instead of choosing the strongest."""
    cuts = {0, size}
    for r in regions:
        if not 0 <= r['start'] < r['end'] <= size:
            raise ValueError(f'Invalid half-open range: {r["id"]}')
        if r['status'] not in STATUSES or r['classification'] not in ('code', 'data', 'unknown'):
            raise ValueError(f'Invalid annotation status/class: {r["id"]}')
        cuts.update((r['start'], r['end']))
    points = sorted(cuts); result = []
    for start, end in zip(points, points[1:]):
        claims = [r for r in regions if r['start'] <= start and end <= r['end']]
        # A broad partial claim must never be silently promoted by an overlapping narrow one.
        status = min((r['status'] for r in claims), key=STATUSES.index) if claims else 'unreviewed'
        classes = {r['classification'] for r in claims}
        kind = next(iter(classes)) if len(classes) == 1 else 'unknown'
        result.append(dict(start=start, end=end, status=status, classification=kind, claims=[r['id'] for r in claims]))
    return result


def validate_annotations(regions, sizes, root=ROOT):
    ids=[r['id']for r in regions]
    if len(ids)!=len(set(ids)):raise ValueError('Duplicate annotation ID')
    for r in regions:
        if r['module'] not in sizes:raise ValueError('Unknown annotation module')
        partition(sizes[r['module']],[r])
        if not r['evidence']:raise ValueError('Annotation needs evidence')
        if r['status']=='ported' and (not r['implementation'] or not any(p.startswith('tests/')for p in r['evidence'])):
            raise ValueError('Ported annotation needs implementation and test evidence')
        for filename in r['evidence']+r['implementation']:
            path=(root/filename).resolve()
            if not path.is_relative_to(root.resolve())or not path.is_file():raise ValueError(f'Missing or external annotation link: {filename}')


def decode_module(payload, roots, fs_targets, is_code, module_is_le=False):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32, CS_GRP_CALL, CS_GRP_JUMP, CS_GRP_RET, CS_GRP_IRET
    from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_FS, X86_REG_INVALID
    decoder = Cs(CS_ARCH_X86, CS_MODE_32); decoder.detail = True
    instructions = {}; owned = {}; tables = {}; edges = []; conflicts = []; queue = deque(roots)
    def one(at):
        return next(decoder.disasm(payload[at:at+15], at, count=1), None)
    def edge(at, target_module, target, kind):
        edges.append((at, target_module, target, kind))
    if is_code:
        while queue:
            at = queue.popleft()
            while 0 <= at < len(payload):
                if at in instructions or any(a <= at < b for a,b in tables): break
                if at in owned:
                    conflicts.append(dict(address=at, reason='entry_inside_instruction', owner=owned[at])); break
                ins = one(at)
                if ins is None:
                    conflicts.append(dict(address=at, reason='undecodable')); break
                if any(a < at+ins.size and at < b for a,b in tables):
                    conflicts.append(dict(address=at, reason='instruction_overlaps_inline_data')); break
                if any(x in owned for x in range(at, at+ins.size)):
                    conflicts.append(dict(address=at, reason='overlapping_decode')); break
                instructions[at] = ins
                for x in range(at, at+ins.size): owned[x] = at
                nxt = at+ins.size; target = None; target_module = None
                transfer = ins.group(CS_GRP_CALL) or ins.group(CS_GRP_JUMP)
                if transfer and ins.operands:
                    op = ins.operands[0]
                    if op.type == X86_OP_IMM:
                        target = op.imm; target_module = 'self'; edge(at,'self',target,'direct-call' if ins.group(CS_GRP_CALL) else 'direct-jump')
                    elif op.type == X86_OP_MEM and op.mem.segment == X86_REG_FS and op.mem.base == X86_REG_INVALID and op.mem.index == X86_REG_INVALID:
                        target = fs_targets.get(op.mem.disp); target_module = 'LE:1'
                        edge(at,'LE:1',target,f'fs:0x{op.mem.disp:x}')
                    else: edge(at,None,None,'unresolved-indirect')
                    if target_module == 'self' and target is not None and 0 <= target < len(payload): queue.append(target)
                    # C88C advances its caller return address by 24, then selects one of six relative entries.
                    if ins.group(CS_GRP_CALL) and ((target_module == 'LE:1' and target == 0xc88c) or (target_module == 'self' and target == 0xc88c and module_is_le)):
                        if nxt+24 <= len(payload):
                            values = struct.unpack_from('<6i',payload,nxt)
                            targets = [nxt+v for v in values]
                            if all(0 <= v < len(payload) for v in targets):
                                tables[(nxt,nxt+24)] = 'C88C six-action relative table'
                                if any(x in owned for x in range(nxt,nxt+24)):conflicts.append(dict(address=nxt,reason='inline_data_overlaps_prior_decode'))
                                for value in targets: edge(at,'self',value,'inline-action-table'); queue.append(value)
                                nxt += 24
                            else: conflicts.append(dict(address=nxt,reason='invalid_inline_action_table'))
                    if ins.mnemonic in ('jmp','ljmp'): break
                if ins.group(CS_GRP_RET) or ins.group(CS_GRP_IRET) or ins.mnemonic in ('hlt','ud2','int3'): break
                at = nxt
    # The complete linear listing is explicitly a candidate interpretation, never a code classification.
    listing = []; at = 0
    while at < len(payload):
        ins = one(at)
        if ins:
            listing.append((at, ins.size, bytes(ins.bytes).hex(), ins.mnemonic, ins.op_str)); at += ins.size
        else:
            listing.append((at,1,payload[at:at+1].hex(),'.byte',f'0x{payload[at]:02x}')); at += 1
    reached = [(at,ins.size,bytes(ins.bytes).hex(),ins.mnemonic,ins.op_str)for at,ins in sorted(instructions.items())]
    return listing, reached, edges, tables, conflicts, len(owned)


def build(game, output, summary_path, annotation_path):
    import capstone
    if output.exists(): raise ValueError('Choose a new output directory; existing analysis is never overwritten')
    if summary_path and summary_path.resolve().parent==game.resolve():raise ValueError('Summary cannot overwrite source game files')
    report, _ = analyze(game)
    expected = json.loads((ROOT/'docs/code-analysis.json').read_text())['source_sha256']
    if report['source_sha256'] != expected: raise ValueError('EXE/DAT/WRK source profile mismatch')
    ledger = json.loads(annotation_path.read_text())
    if ledger['format_version'] != 1: raise ValueError('Unknown annotation schema')
    regions = ledger['regions']
    original = (game/'NORWIND.EXE').read_bytes(); le, objects = read_le_objects(original, INNER_MZ)
    modules = [(f'LE:{o["index"]}',o['payload'],bool(o['flags']&4),'NORWIND.EXE',o['pages'])for o in objects]
    for army in report['armies']:
        name=army['wrk']; data=(game/name).read_bytes(); modules.append((name,data,True,name,[dict(object_offset=0,file_offset=0,bytes=len(data))]))
    validate_annotations(regions,{m:len(data)for m,data,*_ in modules})
    partitions = {m:partition(len(data),[r for r in regions if r['module']==m])for m,data,*_ in modules}
    output.mkdir(parents=True); db=sqlite3.connect(output/'assembly.sqlite')
    db.executescript('''
      CREATE TABLE sources(name TEXT PRIMARY KEY,sha256 TEXT,size INTEGER,payload BLOB);
      CREATE TABLE modules(id TEXT PRIMARY KEY,source TEXT,sha256 TEXT,size INTEGER,is_code INTEGER,payload BLOB);
      CREATE TABLE source_ranges(module TEXT,module_offset INTEGER,file_offset INTEGER,size INTEGER);
      CREATE TABLE annotations(id TEXT PRIMARY KEY,module TEXT,start INTEGER,end INTEGER,name TEXT,status TEXT,classification TEXT,evidence TEXT,implementation TEXT,limitations TEXT);
      CREATE TABLE coverage(module TEXT,start INTEGER,end INTEGER,status TEXT,classification TEXT,claims TEXT);
      CREATE TABLE instructions(module TEXT,address INTEGER,size INTEGER,bytes TEXT,mnemonic TEXT,operands TEXT,view TEXT,PRIMARY KEY(module,address,view));
      CREATE TABLE edges(module TEXT,address INTEGER,target_module TEXT,target INTEGER,kind TEXT);
      CREATE TABLE inline_data(module TEXT,start INTEGER,end INTEGER,reason TEXT);
      CREATE TABLE decode_issues(module TEXT,address INTEGER,detail TEXT);
      CREATE INDEX edges_target ON edges(target_module,target);
    ''')
    for name,digest in sorted(report['source_sha256'].items()):
        data=(game/name).read_bytes();db.execute('INSERT INTO sources VALUES(?,?,?,?)',(name,digest,len(data),data))
    for r in regions:
        db.execute('INSERT INTO annotations VALUES(?,?,?,?,?,?,?,?,?,?)',(r['id'],r['module'],r['start'],r['end'],r['name'],r['status'],r['classification'],json.dumps(r['evidence']),json.dumps(r['implementation']),r['limitations']))
    fs={r['fs_slot']:r['target_offset']for r in report['fs_assignments']}
    entry=struct.unpack_from('<II',original,le+0x18); summaries=[]
    for module,data,is_code,source,pages in modules:
        label=module.replace(':','_');(output/(label+'.bin')).write_bytes(data)
        roots = {r['start']for r in regions if r['module']==module and r['classification']=='code'}
        if module=='LE:1': roots.add(entry[1]); roots.update(fs.values())
        if module.endswith('.WRK'):
            army=next(a for a in report['armies'] if a['wrk']==module);roots.update(c['wrk_offset']for c in army['callbacks'])
        # LE FS initializer destinations are discovery candidates, not independently verified functions.
        local_fs=dict(fs)
        linear,reached,edges,tables,issues,reached_bytes=decode_module(data,roots,local_fs,is_code,module=='LE:1')
        db.execute('INSERT INTO modules VALUES(?,?,?,?,?,?)',(module,source,hashlib.sha256(data).hexdigest(),len(data),is_code,data))
        db.executemany('INSERT INTO source_ranges VALUES(?,?,?,?)',[(module,p['object_offset'],p['file_offset'],p['bytes'])for p in pages])
        for view,rows in [('linear-candidate',linear),('flow-candidate',reached)]:
            db.executemany('INSERT INTO instructions VALUES(?,?,?,?,?,?,?)',[(module,*row,view)for row in rows])
            with (output/(label+'.'+view+'.asm')).open('w') as out:
                out.write('; Object-relative x86/32 CANDIDATE listing. Not original source or proof of code/data boundaries.\n')
                for at,size,raw,mnemonic,operands in rows:out.write(f'{at:08x}: {raw:32s} {mnemonic} {operands}\n')
        with (output/(label+'.seeds.tsv')).open('w') as out:
            out.write('# Discovery candidates; review code/data boundaries in Ghidra.\n')
            for at in sorted(roots):out.write(f'0x{at:x}\tcode\n')
        db.executemany('INSERT INTO edges VALUES(?,?,?,?,?)',[(module,at,module if tm=='self' else tm,target,kind)for at,tm,target,kind in edges])
        db.executemany('INSERT INTO inline_data VALUES(?,?,?,?)',[(module,a,b,why)for (a,b),why in tables.items()])
        db.executemany('INSERT INTO decode_issues VALUES(?,?,?)',[(module,i['address'],json.dumps(i))for i in issues])
        for p in partitions[module]:db.execute('INSERT INTO coverage VALUES(?,?,?,?,?,?)',(module,p['start'],p['end'],p['status'],p['classification'],json.dumps(p['claims'])))
        counts=Counter()
        for p in partitions[module]:counts[p['status']]+=p['end']-p['start']
        summaries.append(dict(module=module,source=source,size=len(data),executable=is_code,source_backed_bytes=sum(p['bytes']for p in pages),zero_fill_bytes=len(data)-sum(p['bytes']for p in pages),sha256=hashlib.sha256(data).hexdigest(),byte_status=dict(sorted(counts.items())),
                              linear_rows=len(linear),flow_candidate_instructions=len(reached),flow_candidate_bytes=reached_bytes,edges=len(edges),unresolved_indirect_edges=sum(e[-1]=='unresolved-indirect'for e in edges),decode_issues=len(issues),inline_table_bytes=sum(b-a for a,b in tables),regions=partitions[module]))
    db.commit();db.close()
    # Account for physical EXE bytes outside LE object payload pages (headers, DOS/container/loader regions).
    mapped=sorted((p['file_offset'],p['file_offset']+p['bytes'])for o in objects for p in o['pages']);cursor=0;gaps=[]
    for start,end in mapped:
        if start>cursor:gaps.append(dict(start=cursor,end=start,status='unreviewed',classification='container_or_loader_unknown'))
        cursor=max(cursor,end)
    if cursor<len(original):gaps.append(dict(start=cursor,end=len(original),status='unreviewed',classification='container_or_loader_unknown'))
    result=dict(format_version=1,annotation_sha256=hashlib.sha256(annotation_path.read_bytes()).hexdigest(),source_sha256=report['source_sha256'],decoder=dict(name='capstone',version=capstone.__version__,mode='x86/32'),
                address_model='Unrelocated object-relative LE modules and WRK modules; no flat runtime DS/FS model',modules=summaries,exe_unmapped_file_ranges=gaps,
                limitations=['Linear and recursive disassembly are candidates, not reviewed code or recovered author source.',
                             'Indirect transfers, inline data, instruction overlaps and external/segmented addresses remain unresolved where reported.',
                             'Progress is annotation coverage only; conditional port limitations remain attached to each annotation.',
                             'DOS/container bytes outside LE pages are accounted for but have not been analyzed as 16-bit code.',
                             'LE non-executable modules contain data; their linear x86 interpretation is diagnostic only.'])
    (output/'coverage.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    if summary_path:summary_path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(dict(modules=len(modules),annotations=len(regions),flow_candidate_instructions=sum(m['flow_candidate_instructions']for m in summaries),database=str(output/'assembly.sqlite'))))


def import_rea(db_path, directory, report_path):
    db=sqlite3.connect(db_path.resolve().as_uri()+'?mode=rw',uri=True)
    db.executescript('CREATE TABLE IF NOT EXISTS rea_evidence(evidence_id TEXT PRIMARY KEY,module TEXT,operation TEXT,payload TEXT); CREATE TABLE IF NOT EXISTS rea_functions(module TEXT,address INTEGER,name TEXT,evidence_id TEXT PRIMARY KEY,body TEXT);\n    CREATE TABLE IF NOT EXISTS rea_instructions(module TEXT,address INTEGER,assembly TEXT,evidence_id TEXT,ordinal INTEGER,PRIMARY KEY(evidence_id,ordinal));')
    summaries=[]
    for source in sorted(directory.glob('*.observations.jsonl')):
        module=None; identities=set(); errors=functions=instructions=0; selectors=set(); inventory_id=None; provider=None
        for line in source.read_text().splitlines():
            row=json.loads(line)
            if module is not None and row['module']!=module:raise ValueError('Mixed module identities in REA observation file')
            module=row['module']
            if not db.execute('SELECT 1 FROM modules WHERE id=?',(module,)).fetchone():raise ValueError('Unknown REA observation module')
            raw=row['evidence']; error=row['is_error'];errors+=error
            if error:continue
            if row['operation'] not in ('list_procedures','read_function_instructions'):continue
            digest=raw['subject']['digest']['sha256']
            expected=db.execute('SELECT sha256 FROM modules WHERE id=?',(module,)).fetchone()
            if not expected or digest!=expected[0]:raise ValueError('REA evidence/module identity mismatch')
            identities.add(digest);provider=raw['provider'];evidence_id=raw['evidence_id'];normalized=raw['normalized_result']
            db.execute('INSERT OR REPLACE INTO rea_evidence VALUES(?,?,?,?)',(evidence_id,module,row['operation'],json.dumps(raw,ensure_ascii=False)))
            if row['operation']=='list_procedures':inventory_id=evidence_id;continue
            proc=normalized['procedure'];address=int(proc['address'],16);selectors.add(address)
            db.execute('INSERT OR REPLACE INTO rea_functions VALUES(?,?,?,?,?)',(module,address,proc['name'],evidence_id,json.dumps(proc.get('body'))));functions+=1
            for i,text in enumerate(normalized['instructions']):
                at,separator,assembly=text.partition(': ')
                if not separator:raise ValueError('Unrecognized REA instruction text')
                db.execute('INSERT OR REPLACE INTO rea_instructions VALUES(?,?,?,?,?)',(module,int(at,16),assembly,evidence_id,i));instructions+=1
        summaries.append(dict(module=module,sha256=next(iter(identities))if identities else None,provider=provider,inventory_evidence_id=inventory_id,
                              exported_functions=functions,instruction_rows=instructions,errors=errors,
                              seed_sha256=hashlib.sha256((directory/(module.replace(':','_')+'.seeds.tsv')).read_bytes()).hexdigest()))
        with (directory/(module.replace(':','_')+'.rea.asm')).open('w')as out:
            out.write('; REA/Ghidra observed instructions. Function boundaries are analysis candidates.\n')
            for at,assembly in db.execute('SELECT address,MIN(assembly) FROM rea_instructions WHERE module=? GROUP BY address ORDER BY address',(module,)):out.write(f'{at:08x}: {assembly}\n')
    db.commit();db.close()
    result=dict(format_version=1,tool=json.loads((directory/'tool.json').read_text())if(directory/'tool.json').exists()else None,modules=summaries,limitations=['REA/Ghidra raw x86/32 modules, object-relative addresses; no complete LE relocation or DS/FS runtime model.',
            'Seeded names/boundaries are candidate analysis metadata; procedure counts and instruction counts do not establish complete function discovery.'])
    if report_path:report_path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(f'Imported REA assembly: {sum(m["exported_functions"]for m in summaries)} functions, {sum(m["instruction_rows"]for m in summaries)} instruction rows')


def sync_annotations(db_path, summary_path, annotation_path):
    ledger=json.loads(annotation_path.read_text());summary=json.loads(summary_path.read_text())
    if ledger['format_version']!=1:raise ValueError('Unknown annotation schema')
    db=sqlite3.connect(db_path.resolve().as_uri()+'?mode=rw',uri=True)
    modules={m:(size,digest)for m,size,digest in db.execute('SELECT id,size,sha256 FROM modules')}
    if dict(db.execute('SELECT name,sha256 FROM sources'))!=summary['source_sha256']:raise ValueError('Coverage/source database mismatch')
    if {m['module']:(m['size'],m['sha256'])for m in summary['modules']}!=modules:raise ValueError('Coverage/module database mismatch')
    regions=ledger['regions'];validate_annotations(regions,{m:v[0]for m,v in modules.items()})
    with db:
        db.execute('DELETE FROM annotations');db.execute('DELETE FROM coverage')
        for r in regions:
            db.execute('INSERT INTO annotations VALUES(?,?,?,?,?,?,?,?,?,?)',(r['id'],r['module'],r['start'],r['end'],r['name'],r['status'],r['classification'],json.dumps(r['evidence']),json.dumps(r['implementation']),r['limitations']))
        for m in summary['modules']:
            parts=partition(m['size'],[r for r in regions if r['module']==m['module']]);counts=Counter()
            for part in parts:
                counts[part['status']]+=part['end']-part['start']
                db.execute('INSERT INTO coverage VALUES(?,?,?,?,?,?)',(m['module'],part['start'],part['end'],part['status'],part['classification'],json.dumps(part['claims'])))
            m['regions']=parts;m['byte_status']=dict(sorted(counts.items()))
    db.close();summary['annotation_sha256']=hashlib.sha256(annotation_path.read_bytes()).hexdigest()
    summary_path.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
    print(f'Synchronized {len(regions)} annotations without re-reading original game files')


def progress(summary_path, output):
    summary=json.loads(summary_path.read_text());ledger=json.loads((ROOT/'data/reverse_engineering/annotations.json').read_text())
    if summary.get('annotation_sha256')!=hashlib.sha256((ROOT/'data/reverse_engineering/annotations.json').read_bytes()).hexdigest():raise ValueError('Annotation ledger changed; regenerate coverage before publishing progress')
    lines=['# Реестр покрытия ассемблера', '', 'Сгенерировано `tools/assembly_database.py report`. Статусы относятся к указанным участкам и ограничениям, а не к готовности всей игры. Полные локальные инструкции и REA Evidence находятся в `build/`; здесь нет оригинальных байтов.', '',
           '| Модуль | Байт | Не разобрано | Частично | Проверено | Перенесено | Неразрешённые косвенные переходы |', '| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
    for m in summary['modules']:
        c=m['byte_status'];lines.append(f'| {m["module"]} | {m["size"]} | {c.get("unreviewed",0)} | {c.get("partial",0)} | {c.get("verified",0)} | {c.get("ported",0)} | {m["unresolved_indirect_edges"]} |')
    lines+=['', 'Неразмеченные данные LE:2/LE:3 тоже остаются в учёте. DOS-контейнер и загрузочные области вне страниц LE перечислены отдельно в [assembly-coverage.json](assembly-coverage.json). Косвенные переходы считаются только среди обнаруженных кандидатов инструкций; неизвестные области могут содержать дополнительные.', '',
            '## Исследованные участки', '', '| Модуль / полуоткрытый диапазон | Имя | Статус | Доказательства | Ограничения |', '| --- | --- | --- | --- | --- |']
    for r in sorted(ledger['regions'],key=lambda r:(r['module'],r['start'])):
        evidence='; '.join(f'[{Path(p).name}](../{p})'for p in r['evidence'])
        name=r['name'].replace('|','/');limitations=r['limitations'].replace('|','/')
        lines.append(f'| {r["module"]} `0x{r["start"]:X}..0x{r["end"]:X}` | {name} | {r["status"]} | {evidence} | {limitations} |')
    lines+=['', 'Порядок работы и определения статусов: [assembly-database.md](assembly-database.md).', '']
    output.write_text('\n'.join(lines),encoding='utf-8')


def main():
    p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
    b=sub.add_parser('build');b.add_argument('--game',required=True,type=Path);b.add_argument('--output',required=True,type=Path);b.add_argument('--summary',type=Path);b.add_argument('--annotations',type=Path,default=ROOT/'data/reverse_engineering/annotations.json')
    s=sub.add_parser('sync');s.add_argument('--db',type=Path,required=True);s.add_argument('--summary',type=Path,default=ROOT/'docs/assembly-coverage.json');s.add_argument('--annotations',type=Path,default=ROOT/'data/reverse_engineering/annotations.json')
    i=sub.add_parser('import-rea');i.add_argument('--db',type=Path,required=True);i.add_argument('--evidence',type=Path,required=True);i.add_argument('--report',type=Path)
    r=sub.add_parser('report');r.add_argument('--summary',type=Path,default=ROOT/'docs/assembly-coverage.json');r.add_argument('--output',type=Path,default=ROOT/'docs/assembly-progress.md')
    q=sub.add_parser('query');q.add_argument('--db',required=True,type=Path);q.add_argument('--module',required=True);q.add_argument('--address',required=True,type=lambda s:int(s,0));q.add_argument('--limit',type=int,default=20);q.add_argument('--view',choices=['flow-candidate','linear-candidate','rea'],default='flow-candidate')
    a=p.parse_args()
    if a.command=='build':build(a.game,a.output,a.summary,a.annotations)
    elif a.command=='sync':sync_annotations(a.db,a.summary,a.annotations)
    elif a.command=='import-rea':import_rea(a.db,a.evidence,a.report)
    elif a.command=='report':progress(a.summary,a.output)
    else:
        db=sqlite3.connect(a.db.resolve().as_uri()+'?mode=ro',uri=True)
        if a.view=='rea':
            for at,assembly in db.execute('SELECT address,MIN(assembly) FROM rea_instructions WHERE module=? AND address>=? GROUP BY address ORDER BY address LIMIT ?',(a.module,a.address,a.limit)):print(f'{at:08x}: {assembly}')
            db.close();return
        for row in db.execute('SELECT address,bytes,mnemonic,operands FROM instructions WHERE module=? AND address>=? AND view=? ORDER BY address LIMIT ?',(a.module,a.address,a.view,a.limit)):print(f'{row[0]:08x}: {row[1]:32s} {row[2]} {row[3]}')
        db.close()

if __name__=='__main__':main()
