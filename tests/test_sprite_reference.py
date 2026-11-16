import sys
import unittest
from pathlib import Path
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from match_sprite_reference import frame_pixels, match_frame
from measure_reference_motion import color_probe


class SpriteReferenceTests(unittest.TestCase):
    def fixture(self):
        palette = bytearray(768)
        palette[3:12] = bytes([255,0,0, 0,255,0, 0,0,255])
        image = Image.new('RGB', (10, 10))
        image.putpixel((3,4), (255,0,0)); image.putpixel((4,4), (0,255,0))
        image.putpixel((3,5), (255,255,0)); image.putpixel((4,5), (0,0,255))
        return image, bytes(palette)

    def test_exact_masked_match_and_full_coverage(self):
        image, palette = self.fixture()
        args = (image, [1,2,-1,3], [2,2], palette, bytes(range(256)))
        result = match_frame(*args, [0,0,10,10], [0,0,10,10])
        self.assertEqual(result['status'], 'unique')
        self.assertEqual(result['screen_top_left_candidates'], [[3,4]])
        self.assertEqual(result['opaque_pixels'], 3)
        self.assertEqual(match_frame(*args, [3,4,1,2], [0,0,10,10])['status'], 'no_match')
        image.putpixel((4,5), (1,1,1))
        self.assertEqual(match_frame(*args, [0,0,10,10], [0,0,10,10])['status'], 'no_match')

    def test_translation_table_and_ambiguity(self):
        image, palette = self.fixture()
        table = bytearray(range(256)); table[1], table[2] = 2,1
        self.assertEqual(match_frame(image,[1,2,-1,3],[2,2],palette,table,[0,0,10,10],[0,0,10,10])['status'], 'no_match')
        for x,y,c in [(6,6,(255,0,0)),(7,6,(0,255,0)),(7,7,(0,0,255))]: image.putpixel((x,y),c)
        self.assertEqual(match_frame(image,[1,2,-1,3],[2,2],palette,bytes(range(256)),[0,0,10,10],[0,0,10,10])['status'], 'ambiguous')

    def test_frame_bounds_and_empty_sprite(self):
        data = b'\1\0\0\0\0\x82\x01\x02'
        self.assertEqual(frame_pixels(data,0),[1,2])
        for bad,index in [(data,1),(b'',0),(data[:-1],0)]:
            with self.assertRaises(ValueError): frame_pixels(bad,index)
        image,palette=self.fixture()
        with self.assertRaises(ValueError): match_frame(image,[-1]*4,[2,2],palette,bytes(range(256)),[0,0,10,10],[0,0,10,10])


class MotionProbeTests(unittest.TestCase):
    def test_centroid_and_no_pixels(self):
        image=Image.new('RGB',(4,4)); image.putpixel((1,1),(255,0,0)); image.putpixel((2,1),(255,0,0))
        result=color_probe(image,[0,0,4,4])
        self.assertEqual(result['centroid'],[1.5,1.0]); self.assertEqual(result['pixel_count'],2)
        self.assertIsNone(color_probe(image,[0,2,4,2])['centroid'])
        with self.assertRaises(ValueError): color_probe(image,[0,0,5,5])


if __name__ == '__main__': unittest.main()
