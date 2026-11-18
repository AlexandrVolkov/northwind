import json
from pathlib import Path
import unittest
from PIL import Image


class SkirmishFramesTests(unittest.TestCase):
    def test_all_worker_strike_frames_are_bundled(self):
        root=Path(__file__).resolve().parents[1]/'assets/imported'
        data=json.loads((root/'battle_frames.json').read_text(encoding='utf-8'))
        entry=json.loads((root/'templates.json').read_text(encoding='utf-8'))['armies']['ARMY__02.DAT']['templates'][10]
        raw=bytes.fromhex(entry['raw_record_hex']);base=int.from_bytes(raw[0x48:0x4a], 'little')
        frames={f['frame']:f for f in data['frames'] if f['template_file']=='ARMY__02.DAT' and f['template_index']==10}
        with Image.open(root/'battle_frames.png') as atlas:
            for heading in range(8):
                for progress in range(raw[0x4b]):
                    frame=frames[base+8*raw[0x4a]+heading*raw[0x4b]+progress]
                    self.assertEqual((frame['width'],frame['height']),(entry['width'],entry['height']))
                    crop=atlas.crop((frame['x'],frame['y'],frame['x']+frame['width'],frame['y']+frame['height']))
                    self.assertIsNotNone(crop.getbbox())

    def test_farm_construction_frames_are_bundled(self):
        root=Path(__file__).resolve().parents[1]/'assets/imported'
        data=json.loads((root/'battle_frames.json').read_text(encoding='utf-8'))
        raw=bytes.fromhex(json.loads((root/'templates.json').read_text(encoding='utf-8'))['armies']['ARMY__02.DAT']['templates'][9]['raw_record_hex'])
        base=int.from_bytes(raw[0x48:0x4a],'little')
        frames={f['frame']for f in data['frames']if f['template_file']=='ARMY__02.DAT'and f['template_index']==9}
        self.assertTrue(set(range(base,base+raw[0x4a]+1)).issubset(frames))

    def test_barracks_construction_frames_are_bundled(self):
        root=Path(__file__).resolve().parents[1]/'assets/imported'
        data=json.loads((root/'battle_frames.json').read_text(encoding='utf-8'))
        raw=bytes.fromhex(json.loads((root/'templates.json').read_text(encoding='utf-8'))['armies']['ARMY__02.DAT']['templates'][3]['raw_record_hex'])
        base=int.from_bytes(raw[0x48:0x4a],'little')
        frames={f['frame']for f in data['frames']if f['template_file']=='ARMY__02.DAT'and f['template_index']==3}
        self.assertTrue(set(range(base,base+raw[0x4a]+1)).issubset(frames))

    def test_mine_construction_frames_are_bundled(self):
        root=Path(__file__).resolve().parents[1]/'assets/imported'
        data=json.loads((root/'battle_frames.json').read_text(encoding='utf-8'))
        raw=bytes.fromhex(json.loads((root/'templates.json').read_text(encoding='utf-8'))['armies']['ARMY__02.DAT']['templates'][6]['raw_record_hex'])
        base=int.from_bytes(raw[0x48:0x4a],'little')
        frames={f['frame']for f in data['frames']if f['template_file']=='ARMY__02.DAT'and f['template_index']==6}
        self.assertTrue(set(range(base,base+raw[0x4a]+1)).issubset(frames))

    def test_unit_and_castle_colors_match_previously_verified_observations(self):
        root=Path(__file__).resolve().parents[1]/'assets/imported'
        data=json.loads((root/'battle_frames.json').read_text(encoding='utf-8'))
        observations=json.loads((root/'observations.json').read_text(encoding='utf-8'))
        with Image.open(root/'battle_frames.png') as atlas:
            for sample in observations['samples']:
                frame=next(f for f in data['frames'] if f['template_file']=='ARMY__02.DAT' and f['frame']==sample['frame'])
                with Image.open(root/sample['image']) as observation:
                    compared=0
                    for y in range(frame['height']):
                        for x in range(frame['width']):
                            if any(ex<=x<ex+w and ey<=y<ey+h for ex,ey,w,h in sample['exclude']):continue
                            pixel=atlas.getpixel((frame['x']+x,frame['y']+y))
                            if pixel[3]==0:continue
                            screen=(sample['world_top_left'][0]-sample['camera'][0]+x,
                                    sample['world_top_left'][1]-sample['camera'][1]+y)
                            self.assertEqual(pixel[:3],observation.getpixel(screen), (sample['id'],x,y))
                            compared+=1
                    self.assertEqual(compared,sample['compared_opaque_pixels'])


if __name__=='__main__':unittest.main()
