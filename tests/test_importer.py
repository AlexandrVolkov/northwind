"""Small synthetic format tests; real game data is optional and never committed."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location('import_game', Path(__file__).resolve().parents[1] / 'tools/import_game.py')
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)


def spr(records):
    offsets, payload = [], b''
    for record in records:
        offsets.append(len(payload).to_bytes(3, 'little'))
        payload += record
    return struct.pack('<H', len(records)) + b''.join(offsets) + payload


class ImportTests(unittest.TestCase):
    def test_transparency_and_literals(self):
        self.assertEqual(importer.decode_spr(spr([b'\x02\x83\x10\x11\x12\x01'])),
                         [[-1, -1, 16, 17, 18, -1]])

    def test_rejects_truncated_literal(self):
        with self.assertRaisesRegex(ValueError, 'literal'):
            importer.decode_spr(spr([b'\x83\x01']))

    def test_rejects_bad_offset(self):
        with self.assertRaises(ValueError):
            importer.decode_spr(b'\x01\x00\xff\xff\xff')

    def test_import_preserves_high_bits_and_pixel_coordinates(self):
        with tempfile.TemporaryDirectory() as temp:
            source, output = Path(temp) / 'game', Path(temp) / 'out'
            source.mkdir()
            palette = bytearray(768)
            palette[3:6] = b'\x10\x80\x20'
            palette[6:9] = b'\x10\x20\x80'
            (source / 'COLOR1.PAL').write_bytes(palette)
            (source / 'MAP_1.MAP').write_bytes(b'MAP2MAPSEG01.SEG\0' + struct.pack('<II2I', 2, 1, 0x10000, 1))
            header = b'SEG2' + struct.pack('<I', 2) + bytes(13)
            (source / 'MAPSEG01.SEG').write_bytes(header + bytes([1, 2]) + bytes(16) + bytes([1]) * 1024 + bytes([2]) * 1024 + b'unknown')
            # 3844 transparent pixels = 30*127 + 34, represented as skip runs.
            frame = bytes([127]) * 30 + bytes([34])
            (source / 'ARMY__01.SPR').write_bytes(spr([frame] * 37))
            manifest = importer.import_directory(source, output)
            import json
            data = json.loads((output / 'map.json').read_text())
            self.assertEqual(data['cells'], [0x10000, 1])
            self.assertEqual(data['segment_unknown_tail_bytes'], 7)
            self.assertEqual(data['terrain'], ['ground', 'water'])
            self.assertEqual(manifest['decoded_sprite_records'], 37)
            with Image.open(output / 'terrain.png') as image:
                self.assertEqual(image.getpixel((0, 0)), (16, 128, 32))
                self.assertEqual(image.getpixel((32, 0)), (16, 32, 128))


class ScenarioTests(unittest.TestCase):
    def fixture(self, count=1):
        header = bytearray(20)
        header[0] = count
        header[2:15] = b'map_1   .map\0'
        records = []
        for _ in range(count):
            record = bytearray(114)
            record[2:15] = b'map_1_01.obe\0'
            record[37:57] = 'Олег'.encode('cp866').ljust(20, b'\xff')
            record[94:114] = 'пос. Олега'.encode('cp866').ljust(20, b'\xff')
            records.append(record)
        return bytes(header) + b''.join(records)

    def test_scenario_names_references_and_lossless_records(self):
        data = self.fixture(2)
        result = importer.decode_scenario(data)
        self.assertEqual(result['slot_count'], 2)
        self.assertEqual(result['map_file'], 'MAP_1.MAP')
        self.assertEqual(result['slots'][0]['leader_name'], 'Олег')
        self.assertEqual(result['slots'][0]['settlement_name'], 'пос. Олега')
        self.assertEqual(result['slots'][0]['object_file'], 'MAP_1_01.OBE')
        self.assertEqual(result['slots'][1]['file_offset'], 134)
        rebuilt = bytes.fromhex(result['raw_header_hex']) + b''.join(bytes.fromhex(s['raw_record_hex']) for s in result['slots'])
        self.assertEqual(rebuilt, data)

    def test_rejects_inconsistent_record_count(self):
        with self.assertRaisesRegex(ValueError, 'count or size'):
            importer.decode_scenario(self.fixture()[:-1])
        with self.assertRaisesRegex(ValueError, 'header'):
            importer.decode_scenario(b'')

    def test_rejects_unsafe_reference(self):
        data = bytearray(self.fixture())
        data[2:15] = b'../evil .map\0'
        with self.assertRaisesRegex(ValueError, 'Unsafe'):
            importer.decode_scenario(data)

    def test_audit_accepts_empty_obe_and_checks_missing_links(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'SCENA_01.SCN').write_bytes(self.fixture())
            (root / 'MAP_1.MAP').write_bytes(b'MAP2MAPSEG01.SEG\0' + struct.pack('<III', 1, 1, 0))
            (root / 'MAPSEG01.SEG').write_bytes(b'SEG2' + struct.pack('<I', 1) + bytes(13 + 9 + 1024))
            (root / 'MAP_1_01.OBE').write_bytes(b'')
            report = importer.audit_directory(root)
            self.assertEqual(report['scenarios'][0]['objects'][0]['bytes'], 0)
            (root / 'MAP_1_01.OBE').unlink()
            with self.assertRaisesRegex(ValueError, 'Missing referenced file: MAP_1_01.OBE'):
                importer.audit_directory(root)


class ObjectTests(unittest.TestCase):
    def record(self, size, x=608, y=720):
        data = bytearray(size)
        data[0] = 2
        struct.pack_into('<II', data, 4, x, y)
        struct.pack_into('<I', data, 16, size)
        return bytes(data)

    def test_variable_records_and_exact_roundtrip(self):
        data = self.record(49) + self.record(91, 100, 0)
        records = importer.decode_objects(data)
        self.assertEqual([r['file_offset'] for r in records], [0, 49])
        self.assertEqual(records[1]['raw_x'], 100)
        self.assertEqual(records[0]['raw_y'], 720)
        self.assertEqual(b''.join(bytes.fromhex(r['raw_record_hex']) for r in records), data)
        self.assertEqual(importer.decode_objects(b''), [])

    def test_rejects_zero_length_short_prefix_and_overrun(self):
        for data in [bytes(34), bytes(33), self.record(49)[:-1]]:
            with self.assertRaises(ValueError):
                importer.decode_objects(data)


class TemplateTests(unittest.TestCase):
    def fixture(self):
        record = bytearray(171)
        struct.pack_into('<I', record, 7, 171 - 7)
        struct.pack_into('<I', record, 0x3b, 0x7f)
        record[0] = 49
        record[0x45] = 127
        struct.pack_into('<H', record, 0x48, 71)
        record[1:3] = bytes([62, 62])
        record[3:5] = bytes([4, 3])
        record[0x58:0x60] = bytes([10, 0, 0, 10, 10, 0, 10, 0])
        struct.pack_into('<4I', record, 0x7c, 50, 80, 150, 2)
        return struct.pack('<H', 171) + bytes(record) + 'Лучник'.encode('cp866') + b'\xff'

    def test_relative_name_pointer_and_preserved_bytes(self):
        data = self.fixture()
        result = importer.decode_templates(data)
        template = result['templates'][0]
        self.assertEqual(template['name'], 'Лучник')
        self.assertEqual(template['name_offset'], 173)
        self.assertEqual(template['file_offset'], 2)
        self.assertEqual(template['wrk_entry_offset'], 0x7f)
        self.assertEqual((template['runtime_record_bytes'], template['initial_condition'], template['initial_frame']), (49, 127, 71))
        self.assertEqual(template['construction'], {'footprint_width': 4, 'footprint_height': 3,
                         'terrain_mask': [10, 0, 0, 10, 10, 0, 10, 0], 'upfront_costs': [50, 80, 150, 2]})
        self.assertEqual(bytes.fromhex(result['raw_header_hex']) + bytes.fromhex(template['raw_record_hex']) + bytes.fromhex(result['raw_tail_hex']), data)

    def test_header_is_not_part_of_first_record_and_records_do_not_overlap(self):
        first = bytearray(self.fixture()[2:173]); second = bytearray(first)
        first[-2:] = b'\xa5\x5a'; second[:1] = b'\x33'; second[-2:] = b'\x12\x34'
        struct.pack_into('<I', first, 7, 342 - 7)
        data = struct.pack('<H', 342) + first + second + b'X\xff'
        result = importer.decode_templates(data)
        self.assertEqual([t['file_offset'] for t in result['templates']], [2, 173])
        self.assertEqual([bytes.fromhex(t['raw_record_hex']) for t in result['templates']], [first, second])

    def test_bad_pointers_boundaries_and_terminators(self):
        for data in [bytes(170), bytes(171), self.fixture()[:-1], struct.pack('<H', 171) + bytes(170)]:
            with self.assertRaises(ValueError):
                importer.decode_templates(data)
        data = bytearray(self.fixture())
        struct.pack_into('<I', data, 9, 99999)
        with self.assertRaises(ValueError):
            importer.decode_templates(data)

    def test_command_slot_relative_pointer_hotkey_and_style_escapes(self):
        data = bytearray(self.fixture())
        pointer = len(data)
        text = b'\x7e\xff' + 'Р'.encode('cp866') + b'\x7e\0' + 'емонт'.encode('cp866')
        data.extend(struct.pack('<H', 0x23) + text + b'\xff')
        row = 2 + 0xb + 8
        struct.pack_into('<HI2B', data, row, 873, pointer-row-2, 0, 7)
        command = importer.decode_templates(data)['templates'][0]['commands'][1]
        self.assertEqual((command['label'], command['hotkey_scan_code'], command['slot']), ('Ремонт', 0x23, 1))
        self.assertEqual(command['raw_group'], 7)
        self.assertEqual(command['tooltip_offset'], pointer)
        struct.pack_into('<H', data, pointer, 0xff)
        self.assertEqual(importer.decode_templates(data)['templates'][0]['commands'][1]['label'], 'Ремонт')
        struct.pack_into('<I', data, row+2, 0xffffffff)
        with self.assertRaises(ValueError): importer.decode_templates(data)

    def test_truncated_command_text_and_style_are_rejected(self):
        data = bytearray(self.fixture()); pointer = len(data)
        row = 2 + 0xb
        struct.pack_into('<HI2B', data, row, 1, pointer-row-2, 0, 0)
        data.extend(b'\x23\0test')
        with self.assertRaises(ValueError): importer.decode_templates(data)
        data.extend(b'\x7e\xff')
        with self.assertRaises(ValueError): importer.decode_templates(data)

    def test_army_slot_maps_through_scenario_not_filename_number(self):
        record = {'raw_code': 4, 'raw_record_hex': bytes(34).hex()}
        importer.link_templates([record], {'ARMY__05.DAT': importer.decode_templates(self.fixture())},
                                ['ARMY__00.DAT', 'ARMY__01.DAT', 'ARMY__02.DAT', 'ARMY__03.DAT', 'ARMY__05.DAT'])
        self.assertEqual(record['template_file'], 'ARMY__05.DAT')
        self.assertEqual(record['template_name'], 'Лучник')
        raw = bytearray(34); raw[14] = 9
        with self.assertRaises(ValueError):
            importer.link_templates([{'raw_code': 0, 'raw_record_hex': raw.hex()}],
                                    {'ARMY__05.DAT': importer.decode_templates(self.fixture())}, ['ARMY__05.DAT'])


class PreviewTests(unittest.TestCase):
    def test_rejected_rle_is_reported_then_exact_dimensions_selected(self):
        with tempfile.TemporaryDirectory() as directory:
            table = {'templates': [{'name': 'Candidate', 'width': 2, 'height': 2, 'sprite_base_candidate': 0}]}
            data = spr([b'\x84\x01', b'\x84\x01\x02\x03\x04'])
            palette = bytes([0, 0, 0, 10, 20, 30]) + bytes(762)
            importer.write_template_previews({'ARMY__01.DAT': table}, lambda _: data, palette, Path(directory))
            preview = table['templates'][0]['preview']
            self.assertEqual(preview['status'], 'candidate')
            self.assertEqual(preview['frame'], 1)
            self.assertIn('literal', preview['rejections'][0]['reason'])
            with Image.open(Path(directory) / 'template_previews.png') as image:
                self.assertEqual(image.getpixel((0, 0)), (10, 20, 30, 255))

    def test_mismatched_frames_remain_unresolved(self):
        with tempfile.TemporaryDirectory() as directory:
            table = {'templates': [{'name': 'Unknown', 'width': 3, 'height': 3, 'sprite_base_candidate': 0}]}
            importer.write_template_previews({'ARMY__01.DAT': table}, lambda _: spr([b'\x04']), bytes(768), Path(directory))
            preview = table['templates'][0]['preview']
            self.assertEqual(preview['status'], 'unresolved')
            self.assertIsNone(preview['frame'])
            self.assertIn('dimensions', preview['rejections'][0]['reason'])


if __name__ == '__main__':
    unittest.main()
