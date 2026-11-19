import json
from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from music_source import midi_containers, smacker_audio, sound_call


class MusicSourceTest(unittest.TestCase):
    def test_relative_sound_call_and_rejection(self):
        code = b'\xb8' + struct.pack('<I', 5) + b'\xbb' + struct.pack('<I', 3) + b'\xe8' + struct.pack('<i', 100 - 15)
        self.assertEqual(sound_call(code, 0, 100, bx=3), 5)
        with self.assertRaises(ValueError): sound_call(code, 0, 101, bx=3)
        with self.assertRaises(ValueError): sound_call(code, 0, 100, bx=1)
        with self.assertRaises(ValueError): sound_call(code[:9], 0, 100, bx=3)

    def test_midi_headers_are_structural(self):
        smf = b'MThd' + struct.pack('>IHHH', 6, 0, 1, 96) + b'MTrk' + struct.pack('>I', 4) + b'\x00\xff\x2f\x00'
        xmi = b'FORM' + struct.pack('>I', 4) + b'XMID'
        self.assertEqual(midi_containers(smf), [{'kind': 'SMF', 'offset': 0}])
        self.assertEqual(midi_containers(xmi), [{'kind': 'XMIDI', 'offset': 0}])
        cmf = bytearray(b'CTMF' + bytes(77))
        struct.pack_into('<HHH', cmf, 4, 0x101, 40, 80)
        self.assertEqual(midi_containers(cmf), [{'kind': 'CMF', 'offset': 0}])
        self.assertEqual(midi_containers(smf[:-1]), [])
        self.assertEqual(midi_containers(b'MThd music FORM XMID CTMF'), [])

    def test_smacker_track_flags(self):
        data = bytearray(b'SMK2' + bytes(100))
        struct.pack_into('<I', data, 72, 0xd0005622)
        self.assertEqual(smacker_audio(data), [dict(track=0, raw_rate_word=0xd0005622, sample_rate=22050,
                                                   channels=2, bits_per_sample=8, packed=True)])
        with self.assertRaises(ValueError): smacker_audio(data[:100])

    def test_prepared_profiles_link_to_bundled_pcm(self):
        root = Path(__file__).resolve().parents[1]
        scenes = json.loads((root / 'data/original_audio_scenes.json').read_text())
        manifest = json.loads((root / 'assets/imported/sound/manifest.json').read_text())
        for source in scenes['banks']:
            bank = next(b for b in manifest['banks'] if b['source'] == source['source'])
            self.assertEqual(bank['source_sha256'], source['source_sha256'])
            for sound in source['sounds']:
                for sample in sound['variants']:
                    actual = next(s for s in bank['sounds'][sound['sound']]['variants'] if s['file'] == sample['file'])
                    self.assertEqual((actual['pcm_sha256'], actual['frames']), (sample['pcm_sha256'], sample['frames']))
        self.assertEqual(scenes['inventory']['midi_family_candidates'], [])
        self.assertEqual(next(p for p in scenes['menu_profiles'] if p['role'] == 'intro_history')['sound'], 0)


if __name__ == '__main__':
    unittest.main()
