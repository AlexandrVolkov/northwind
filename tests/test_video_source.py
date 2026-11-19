from fractions import Fraction
import hashlib
import json
from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from video_source import conversion_command, smk_header, theora_frames, verify_video


class VideoSourceTest(unittest.TestCase):
    def test_header_signed_frame_delay_and_audio(self):
        raw = bytearray(b'SMK2' + bytes(100))
        struct.pack_into('<IIIiI', raw, 4, 640, 480, 350, -3333, 0)
        struct.pack_into('<I', raw, 72, 0xd0005622)
        header = smk_header(raw)
        self.assertEqual(Fraction(header['frame_rate']), Fraction(100000, 3333))
        self.assertEqual(header['duration_seconds'], 11.6655)
        self.assertEqual(header['audio_tracks'][0]['sample_rate'], 22050)
        struct.pack_into('<i', raw, 16, 40)
        self.assertEqual(smk_header(raw)['frame_rate'], '25')
        for offset in [4, 8, 12, 16]:
            bad = bytearray(raw); struct.pack_into('<I', bad, offset, 0)
            with self.assertRaises(ValueError): smk_header(bad)

    def test_probe_rejects_lost_frames_and_sound(self):
        header = dict(width=2, height=2, frames=3, frame_rate='25', duration_seconds=.12, audio_tracks=[])
        info = dict(streams=[dict(codec_type='video', codec_name='theora', width=2, height=2, r_frame_rate='25/1', nb_read_frames='3')],
                    format=dict(duration='0.12'))
        verify_video(header, info, output=True, presentation_frames=3)
        with self.assertRaises(ValueError): verify_video(header, info, output=True, presentation_frames=2)
        header['audio_tracks'] = [dict(sample_rate=22050, channels=2)]
        with self.assertRaises(ValueError): verify_video(header, info, output=True, presentation_frames=3)

    def test_theora_granule_includes_duplicate_frames(self):
        def page(packet, flags, granule):
            return b'OggS\0' + bytes([flags]) + struct.pack('<QIII', granule, 7, 0, 0) + bytes([1, len(packet)]) + packet
        header = bytearray(b'\x80theora' + bytes([3, 2, 1]) + bytes(32))
        header[41] = 6 << 5  # keyframe granule shift = 6
        data = page(header, 2, 0) + page(b'x', 4, (300 << 6) | 50)
        self.assertEqual(theora_frames(data), 350)
        with self.assertRaises(ValueError): theora_frames(data[:-1])
        with self.assertRaises(ValueError): theora_frames(page(header, 2, 0))

    def test_command_handles_paths_without_shell_and_keeps_video_geometry(self):
        command = conversion_command(Path('/tmp/source with spaces.smk'), Path('/tmp/out.ogv'))
        self.assertIn('/tmp/source with spaces.smk', command)
        self.assertIn('0:a:0?', command)
        self.assertNotIn('-r', command)
        self.assertNotIn('-s', command)
        self.assertNotIn('-vf', command)

    def test_prepared_movies_match_manifest(self):
        output = Path(__file__).resolve().parents[1] / 'assets/imported/video'
        manifest = json.loads((output / 'manifest.json').read_text())
        self.assertTrue(manifest['encoder']['lossy'])
        for movie in manifest['videos']:
            raw = (output / movie['file']).read_bytes()
            self.assertTrue(raw.startswith(b'OggS'))
            self.assertIn(b'\x80theora', raw[:100])
            self.assertEqual(len(raw), movie['output_bytes'])
            self.assertEqual(hashlib.sha256(raw).hexdigest(), movie['output_sha256'])
            reference = movie['reference']
            png = (output / reference['file']).read_bytes()
            self.assertEqual(hashlib.sha256(png).hexdigest(), reference['sha256'])
            self.assertTrue(png.startswith(b'\x89PNG\r\n\x1a\n'))
            self.assertEqual(theora_frames(raw), movie['presentation_frames'])
            verify_video(movie['header'], movie['output_probe'], output=True, presentation_frames=movie['presentation_frames'])
            verify_video(movie['header'], movie['source_probe'])


if __name__ == '__main__':
    unittest.main()
