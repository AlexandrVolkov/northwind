import importlib.util
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from assembly_database import partition, decode_module, validate_annotations, sync_annotations
from export_rea_assembly import evidence


def region(start,end,status='partial',name='r'):
    return dict(id=name,start=start,end=end,status=status,classification='code')


class CoverageTests(unittest.TestCase):
    def test_all_bytes_accounted_once(self):
        parts=partition(20,[region(4,8),region(12,18,'ported','p')])
        self.assertEqual([(p['start'],p['end'])for p in parts],[(0,4),(4,8),(8,12),(12,18),(18,20)])
        self.assertEqual(sum(p['end']-p['start']for p in parts),20)
        self.assertEqual(parts[0]['status'],'unreviewed')
    def test_overlap_does_not_upgrade_partial_claim(self):
        parts=partition(12,[region(2,10),region(4,8,'ported','p')])
        middle=next(p for p in parts if p['start']==4)
        self.assertEqual(middle['status'],'partial');self.assertEqual(middle['claims'],['r','p'])
    def test_invalid_ranges_rejected(self):
        for start,end in [(-1,3),(3,3),(5,4),(0,13)]:
            with self.subTest(start=start,end=end),self.assertRaises(ValueError):partition(12,[region(start,end)])
    def test_unknown_status_rejected(self):
        with self.assertRaises(ValueError):partition(10,[region(1,2,'done')])
    def test_rea_envelope_formats(self):
        self.assertEqual(evidence(dict(structuredContent=dict(evidence_id='e'))),dict(evidence_id='e'))
        self.assertEqual(evidence(dict(content=[dict(type='text',text='{"evidence_id":"e"}')])),dict(evidence_id='e'))

    def test_annotation_module_and_duplicate_identity(self):
        r=dict(region(0,1),module='m',evidence=['docs/game-logic-map.md'],implementation=[],limitations='')
        validate_annotations([r],{'m':5})
        with self.assertRaises(ValueError):validate_annotations([r],{'other':5})
        with self.assertRaises(ValueError):validate_annotations([r,r],{'m':5})
    def test_ported_requires_implementation_and_test(self):
        r=dict(region(0,1,'ported'),module='m',evidence=['docs/game-logic-map.md'],implementation=[])
        with self.assertRaises(ValueError):validate_annotations([r],{'m':5})
    def test_missing_evidence_link_rejected(self):
        r=dict(region(0,1),module='m',evidence=['docs/nonexistent-proof.json'],implementation=[])
        with self.assertRaises(ValueError):validate_annotations([r],{'m':5})

    def test_sync_preserves_analysis_and_accounts_changed_status(self):
        import json, sqlite3, tempfile
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);db_path=root/'test.sqlite';c=sqlite3.connect(db_path)
            c.executescript("CREATE TABLE sources(name TEXT,sha256 TEXT); INSERT INTO sources VALUES('s','source'); CREATE TABLE modules(id TEXT,size INTEGER,sha256 TEXT); INSERT INTO modules VALUES('m',10,'module'); CREATE TABLE annotations(id,module,start,end,name,status,classification,evidence,implementation,limitations); CREATE TABLE coverage(module,start,end,status,classification,claims); CREATE TABLE retained_analysis(value); INSERT INTO retained_analysis VALUES(77);")
            c.commit();c.close()
            summary=root/'summary.json';summary.write_text(json.dumps(dict(source_sha256={'s':'source'},modules=[dict(module='m',size=10,sha256='module')])))
            annotation=root/'a.json';r=dict(region(2,5),module='m',name='test',evidence=['docs/game-logic-map.md'],implementation=[],limitations='bounded')
            annotation.write_text(json.dumps(dict(format_version=1,regions=[r])))
            sync_annotations(db_path,summary,annotation)
            c=sqlite3.connect(db_path)
            self.assertEqual(c.execute('SELECT * FROM retained_analysis').fetchall(),[(77,)])
            self.assertEqual(c.execute('SELECT sum(end-start) FROM coverage').fetchone(),(10,))
            self.assertEqual(json.loads(summary.read_text())['modules'][0]['byte_status'],{'partial':3,'unreviewed':7});c.close()


@unittest.skipUnless(importlib.util.find_spec('capstone'),'Optional research dependency capstone')
class DecoderTests(unittest.TestCase):
    def test_inline_table_skipped_in_flow_but_retained(self):
        import struct
        payload=bytes.fromhex('6467ff164100')+struct.pack('<6i',*([24]*6))+b'\xc3'
        linear,flow,edges,tables,issues,count=decode_module(payload,[0],{65:0xc88c},True)
        self.assertEqual([r[0]for r in flow],[0,30]);self.assertEqual(count,7)
        self.assertIn((6,30),tables);self.assertEqual(sum(r[1]for r in linear),len(payload))
        self.assertEqual(len([e for e in edges if e[-1]=='inline-action-table']),6)
    def test_jump_gap_stays_out_of_flow(self):
        linear,flow,edges,*_=decode_module(b'\xeb\x02\x0f\x0b\xc3',[0],{},True)
        self.assertEqual([r[0]for r in flow],[0,4]);self.assertEqual(sum(r[1]for r in linear),5)
    def test_unresolved_indirect_transfer_not_fabricated(self):
        _,flow,edges,*_=decode_module(b'\xff\xd0\xc3',[0],{},True)
        self.assertEqual(edges,[(0,None,None,'unresolved-indirect')]);self.assertEqual(len(flow),2)
    def test_non_executable_data_has_no_discovered_code(self):
        _,flow,edges,*_=decode_module(b'\x90\xc3',[0],{},False)
        self.assertEqual(flow,[]);self.assertEqual(edges,[])
