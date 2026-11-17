import json
from pathlib import Path
import unittest
from PIL import Image


class SkirmishFramesTests(unittest.TestCase):
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
