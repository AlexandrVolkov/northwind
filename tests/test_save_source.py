import json
from pathlib import Path
import struct
import unittest
from tools.save_source import parse, parse_area22, parse_scenario11, parse_session99


class SaveSourceChecks(unittest.TestCase):
    def test_tagged_records_and_payload_bounds(self):
        file = struct.pack('<I', 3) + struct.pack('<II', 3, 0x33) + b'abc' + \
               struct.pack('<II', 0, 0x55) + struct.pack('<II', 4, 0x44) + b'xyz!'
        result = parse(file)
        self.assertEqual([(r['header_offset'], r['payload_offset'], r['length'], r['tag']) for r in result['records']],
                         [(4, 12, 3, 0x33), (15, 23, 0, 0x55), (23, 31, 4, 0x44)])
        for invalid in (file[:-1], file + b'!', b'\0\0\0', struct.pack('<I', 4097)):
            with self.assertRaises(ValueError): parse(invalid)

    def test_empty_victory_marker_is_a_distinct_tag(self):
        file = struct.pack('<I', 1) + struct.pack('<II', 0, 0x66)
        record = parse(file)['records'][0]
        self.assertEqual(record['length'], 0)
        self.assertTrue(record['victory_path_marker'])

    def test_scenario11_header_and_square_exploration_boundary(self):
        body = bytearray(0xF9D + 16 + 4*16 + 12*16 + 0x10000 + 0x2000 + 0x8000 + 0x1A35 + 3)
        body[0x14:0x21] = b'game    .epi\0'
        body[0x22] = 4
        body[0x23] = 5
        body[0x2c:0x2f] = bytes((1, 1, 1))
        body[0xF9D:0xF9D+16] = bytes(range(16))
        result = parse_scenario11(body)
        self.assertEqual((result['episode_filename'], result['width'], result['height']),
                         ('game    .epi', 4, 5))
        self.assertEqual((result['visibility_length'], result['variable_lists_length']), (16, 3))
        self.assertEqual(len(result['fixed_segments']), 7)
        self.assertEqual(result['participants'][-1]['offset'] + 0xF5, result['header_length'])
        for invalid in (body[:0xF9C], body[:0xF9D+15]):
            with self.assertRaises(ValueError): parse_scenario11(invalid)

    def test_campaign99_cabinet_name_records_keep_raw_tail_bytes(self):
        body = bytearray(25 + 512 + 0x841)
        body[25 + 6:25 + 6 + 20] = 'Олег'.encode('cp866') + bytes([0xff] * 16)
        body[25 + 6 + 20:25 + 6 + 22] = b'\x02\x03'
        result = parse_session99(body)
        self.assertEqual((result['cabinet_participants'][0]['name'],
                          result['cabinet_participants'][0]['trailing_bytes']), ('Олег', [2, 3]))
        self.assertEqual(len(result['cabinet_participants']), 16)


    def test_area22_descriptor_and_bucket_bounds(self):
        descriptor = struct.pack('<16I', 0, 0, 0, 0, 80, *([0] * 11))
        payload = bytes(0xF50) + descriptor + bytes(4 * 0x2000) + bytes(range(80)) + bytes(0x2000 - 80)
        result = parse_area22(payload)
        self.assertEqual((result['bucket_count'], result['bucket_size']), (5, 0x2000))
        self.assertEqual(result['descriptor_counts'][4], 80)
        for invalid in (payload[:-1], bytes(0xF50), bytes(0xF50) + struct.pack('<I', 0x2001) + bytes(60 + 0x2000)):
            with self.assertRaises(ValueError): parse_area22(invalid)

    def test_recovered_original_layout_has_no_gaps(self):
        root = Path(__file__).resolve().parents[1]
        data = json.loads((root / 'data/original_save_layout.json').read_text())
        records = data['records']
        self.assertEqual([r['tag'] for r in records], [0x11, 0x99, 0x01000022, 0x00010022])
        self.assertEqual([(r['area_x'], r['area_y']) for r in (records[0], records[2], records[3])],
                         [(0, 0), (0, 1), (1, 0)])
        self.assertEqual(records[0]['scenario11']['episode_filename'], 'game    .epi')
        self.assertEqual((records[0]['scenario11']['width'], records[0]['scenario11']['height']), (128, 128))
        scenario = records[0]['scenario11']
        self.assertEqual(scenario['visibility_length'], 16384)
        self.assertEqual((scenario['variable_list_count'], scenario['variable_lists_length']), (5, 29296))
        self.assertEqual((scenario['field_190e'], scenario['field_1912']), (480, 1152))
        self.assertEqual((scenario['field_1906'], scenario['field_190a']), (15, 36))
        self.assertEqual(len(scenario['participants']), 16)
        self.assertEqual(scenario['participants'][0]['balance_fields_18_1c_20'], [1976, 1976, 0])
        self.assertEqual(scenario['variable_lists_offset'] + scenario['variable_lists_length'], records[0]['length'])
        self.assertEqual([r['length'] for r in records], [988085, 25893, 69520, 69520])
        self.assertEqual([x['tre_file_size'] for x in scenario['tree_banks']], [632, 404, 759, 18, 18])
        self.assertEqual([x['length'] for x in scenario['tree_banks']], [10112, 6464, 12144, 288, 288])
        self.assertEqual(scenario['tree_banks'][0]['offset'], scenario['variable_lists_offset'])
        self.assertEqual(scenario['tree_banks'][-1]['offset'] + scenario['tree_banks'][-1]['length'], records[0]['length'])
        modified = [(bank['index'], copy['index'], copy['differing_bytes'])
                    for bank in scenario['tree_banks'] for copy in bank['copies'] if copy['differing_bytes']]
        self.assertEqual(modified, [(1, 11, 7), (2, 12, 18)])
        self.assertTrue(all(not copy['other_changed_offsets'] for bank in scenario['tree_banks']
                            for copy in bank['copies'] if copy['differing_bytes']))
        self.assertEqual(sum((change['kind_before'] != change['kind_after']) +
                             (change['unmet_before'] != change['unmet_after'])
                             for bank in scenario['tree_banks'] for copy in bank['copies']
                             if copy['differing_bytes'] for change in copy['tech_changes']), 25)
        self.assertEqual(records[0]['header_offset'], 4)
        self.assertEqual([r['payload_offset'] + r['length'] for r in records[:-1]],
                         [r['header_offset'] for r in records[1:]])
        self.assertEqual(records[-1]['payload_offset'] + records[-1]['length'], data['file_size'])
        section = records[1]['campaign99']
        self.assertEqual(section['region_count'], 12)
        self.assertEqual((section['world_width'], section['world_height'],
                          section['current_area_x'], section['current_area_y']), (4, 3, 0, 0))
        self.assertEqual([area['flag'] for area in section['campaign_areas']], [1] + [0] * 11)
        self.assertTrue(all(len(area['participant_sha256']) == 16 and area['participant_record_size'] == 0x84
                            for area in section['campaign_areas']))
        self.assertEqual((section['cabinet_summary_offset_in_table'], section['cabinet_summary_length']), (6, 0x160))
        self.assertEqual([x['name'] for x in section['cabinet_participants'][:4]], ['Олег', 'Игорь', 'Эйрик', ''])
        self.assertEqual(section['cabinet_participants'][2]['trailing_bytes'], [2, 2])
        self.assertEqual(section['region_record_size'], 0x841)
        self.assertEqual(len(section['region_sha256']), 12)
        self.assertEqual(25 + section['table_length'] + section['region_count'] * 0x841, records[1]['length'])
        for area in records[2:]:
            block = area['area22']
            self.assertEqual(block['bucket_count'], 8)
            self.assertEqual(len(block['participant_transfers']), 16)
            self.assertTrue(all(entry['balance_fields_18_1c_20'] == [0, 0, 0] and
                                entry['token_slots_nonzero'] == 0 for entry in block['participant_transfers']))
            self.assertEqual(block['descriptor_counts'][4], 80)
            self.assertEqual([x['length'] for x in block['allocated_suffixes']], [0, 0, 0, 0, 80, 0, 0, 0])
            objects = block['allocated_suffixes'][4]['object_records']
            self.assertEqual([(x['offset_in_suffix'], x['size'], x['army'], x['template'], x['owner_byte'])
                              for x in objects], [(0, 40, 0, 0, 4), (40, 40, 0, 0, 4)])
            self.assertNotIn('unparsed_reason', block['allocated_suffixes'][4])
            self.assertEqual(0xF50 + 0x40 + 8 * 0x2000, area['length'])
        self.assertEqual([area['area22']['allocated_suffixes'][4]['nonzero_bytes'] for area in records[2:]],
                         [25, 27])

    def test_original_post_load_save_keeps_diplomacy_contact_evidence(self):
        root = Path(__file__).resolve().parents[1]
        before = json.loads((root / 'data/original_save_layout.json').read_text())
        after = json.loads((root / 'data/original_save_after_load_layout.json').read_text())
        scenario = after['records'][0]['scenario11']
        self.assertEqual([(scenario['participants'][i]['name'], scenario['participants'][i]['leader_portrait_byte'])
                          for i in (0, 3, 4)], [('Олег', 1), ('Илья', 5), ('Теймур', 3)])
        self.assertEqual([(i, scenario['participants'][0]['relations'][i]) for i in (3, 4, 7)],
                         [(3, 2), (4, 2), (7, 1)])
        self.assertEqual([scenario['stat_field_6ba_19f1'][i] for i in (3, 4, 7)], [26090, 5000, 0])
        self.assertEqual([before['records'][i]['payload_sha256'] == after['records'][i]['payload_sha256']
                          for i in range(4)], [False, False, True, True])


if __name__ == '__main__': unittest.main()
