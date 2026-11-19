import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import wave

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from sound_source import decode_bank, export, mean_level, SAMPLE_RATE


def table(records):
    at = 2 + len(records) * 4
    offsets = []
    for record in records:
        offsets.append(at)
        at += len(record)
    return struct.pack('<H', len(records)) + struct.pack(f'<{len(records)}I', *offsets) + b''.join(records)


def bank(sounds):
    groups = []
    for samples in sounds:
        group = table([struct.pack('<I', len(pcm) | flags << 24) + pcm for flags, pcm in samples])
        groups.append(struct.pack('<I', len(group)) + group)
    return table(groups)


class SoundSourceTest(unittest.TestCase):
    def test_nested_offsets_flags_and_unsigned_pcm(self):
        data = bank([[(0, b'\x00\x80\xff'), (0x81, b'\x91\x10')], [(0, b'\x7f')]])
        sounds = decode_bank(data)
        self.assertEqual([len(s) for s in sounds], [2, 1])
        self.assertEqual(sounds[0][1].raw_high_byte, 0x81)
        self.assertEqual(sounds[0][1].pcm, b'\x91\x10')
        self.assertEqual(sounds[1][0].sound, 1)
        for sound in sounds:
            for sample in sound:
                at = sample.header_offset
                self.assertEqual(data[at + 4:at + 4 + len(sample.pcm)], sample.pcm)

    def test_rejects_truncation_and_inconsistent_sizes(self):
        data = bank([[(0, b'\x00\x80\xff')]])
        for end in range(len(data)):
            with self.subTest(end=end), self.assertRaises(ValueError):
                decode_bank(data[:end])
        for at, value in [(2, 0), (2, 0xffffffff), (6, 0), (12, 0xffffffff), (16, 1)]:
            bad = bytearray(data)
            struct.pack_into('<I', bad, at, value)
            with self.subTest(at=at), self.assertRaises(ValueError):
                decode_bank(bad)
        with self.assertRaises(ValueError):
            decode_bank(data + b'\0')
        with self.assertRaises(ValueError):
            decode_bank(bank([[]]))
        with self.assertRaises(ValueError):
            decode_bank(bank([[(0, b'x')] * 256]))
        self.assertEqual(decode_bank(b'\0\0'), [])

    def test_export_wav_roundtrip_and_deterministic_manifest(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            pcm = b'\x00\x7f\x80\xff\x81'
            (root / 'WORK.FX').write_bytes(bank([[(0, pcm)]]))
            output = root / 'out'
            manifest = export(root, output, ['WORK.FX'])
            sample = manifest['banks'][0]['sounds'][0]['variants'][0]
            wav_bytes = (output / sample['file']).read_bytes()
            self.assertEqual(len(wav_bytes) % 2, 0)
            self.assertEqual(struct.unpack_from('<I', wav_bytes, 4)[0], len(wav_bytes) - 8)
            with wave.open(str(output / sample['file']), 'rb') as stream:
                self.assertEqual((stream.getnchannels(), stream.getsampwidth(), stream.getframerate()), (1, 1, 11025))
                self.assertEqual(stream.getnframes(), len(pcm))
                self.assertEqual(stream.readframes(100), pcm)
            self.assertEqual(sample['pcm_sha256'], hashlib.sha256(pcm).hexdigest())
            self.assertEqual(sample['duration_seconds'], len(pcm) / SAMPLE_RATE)
            sidecar = output / (sample['file'] + '.import')
            self.assertIn('compress/mode=0', sidecar.read_text())
            original_settings = sidecar.read_text().replace('type="AudioStreamWAV"', 'type="AudioStreamWAV"\nuid="uid://test"')
            sidecar.write_text(original_settings.replace('compress/mode=0', 'compress/mode=2'))
            self.assertEqual(json.loads((output / 'manifest.json').read_text()), manifest)
            first = (output / 'manifest.json').read_bytes()
            self.assertEqual(export(root, output, ['WORK.FX']), manifest)
            self.assertEqual((output / 'manifest.json').read_bytes(), first)
            self.assertEqual(sidecar.read_text(), original_settings)
            with self.assertRaises(ValueError):
                export(root, output, ['../escape.FX'])

    def test_mean_level_preserves_native_asymmetry_at_silence(self):
        self.assertEqual(mean_level(bytes([0, 127, 128, 255])), 63)
        self.assertEqual(mean_level(bytes([127, 128])), 0)
        self.assertEqual(mean_level(bytes([0, 255])), 127)
        with self.assertRaises(ValueError):
            mean_level(b'')

    def test_bundled_banks_have_lossless_wavs_and_safe_import_settings(self):
        output = Path(__file__).resolve().parents[1] / 'assets/imported/sound'
        manifest = json.loads((output / 'manifest.json').read_text())
        total = 0
        for source in manifest['banks']:
            self.assertEqual(source['sound_count'], len(source['sounds']))
            for sound in source['sounds']:
                for sample in sound['variants']:
                    path = output / sample['file']
                    raw = path.read_bytes()
                    self.assertEqual(hashlib.sha256(raw).hexdigest(), sample['wav_sha256'])
                    self.assertEqual(struct.unpack_from('<I', raw, 4)[0], len(raw) - 8)
                    self.assertEqual(len(raw) % 2, 0)
                    with wave.open(str(path), 'rb') as stream:
                        self.assertEqual((stream.getnchannels(), stream.getsampwidth(), stream.getframerate()), (1, 1, 11025))
                        self.assertEqual(stream.getnframes(), sample['frames'])
                        pcm = stream.readframes(sample['frames'])
                    self.assertEqual(hashlib.sha256(pcm).hexdigest(), sample['pcm_sha256'])
                    self.assertEqual(mean_level(pcm), sample['mean_level'])
                    settings = path.with_suffix('.wav.import').read_text()
                    for entry in ['compress/mode=0', 'edit/trim=false', 'edit/normalize=false', 'force/max_rate=false', 'edit/loop_mode=0']:
                        self.assertIn(entry, settings)
                    total += 1
        self.assertEqual(total, 166)


if __name__ == '__main__':
    unittest.main()
