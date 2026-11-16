"""Build frozen observations from verified source bytes, never from screenshots."""
import hashlib
import json
from pathlib import Path
from PIL import Image
from match_reference import load_map


def exclude_pixels(pixels, size, regions):
    width, height = size
    result = list(pixels)
    if len(result) != width * height:
        raise ValueError('Invalid frame dimensions')
    for x, y, w, h in regions:
        if min(x,y) < 0 or min(w,h) <= 0 or x+w > width or y+h > height:
            raise ValueError('Exclusion outside frame')
        for yy in range(y,y+h):
            result[yy*width+x:yy*width+x+w] = [-1]*w
    return result


def composite(terrain, pixels, palette, table, sample):
    width, height = sample['size']
    if len(palette) != 768 or len(table) != 256 or len(pixels) != width*height:
        raise ValueError('Invalid palette or frame')
    x,y = sample['camera']; w,h = sample['viewport_size']
    if min(x,y) < 0 or min(w,h) <= 0 or x+w > terrain.width or y+h > terrain.height:
        raise ValueError('Observation viewport outside terrain')
    pixels = exclude_pixels(pixels, sample['size'], sample['exclude'])
    if sum(c >= 0 for c in pixels) != sample['compared_opaque_pixels']:
        raise ValueError('Observation mask differs from verified count')
    sprite = Image.new('RGBA',(width,height))
    sprite.putdata([tuple((v&252)|(v>>6) for v in palette[table[c]*3:table[c]*3+3])+(255,) if c >= 0 else (0,0,0,0) for c in pixels])
    result = terrain.crop((x,y,x+w,y+h)).convert('RGB')
    position = (sample['world_top_left'][0]-x, sample['world_top_left'][1]-y)
    result.paste(sprite, position, sprite)
    return result


def render_observations(source, destination, map_number, decode_frame, specification=None):
    if specification is None:
        specification = json.loads((Path(__file__).resolve().parents[1]/'docs/render-observations.json').read_text(encoding='utf-8'))
    result = {'format_version':1,'status':'unavailable','samples':[],
              'note':'Independent frozen observations; not a synchronized game scene or live simulation.'}
    files = {p.name.upper():p for p in source.iterdir() if p.is_file()}
    if map_number != specification['map_number']:
        result['reason']='Reference is for another map'
    elif any(name not in files or hashlib.sha256(files[name].read_bytes()).hexdigest()!=digest for name,digest in specification['source_sha256'].items()):
        result['reason']='Source hashes differ from verified inputs'
    else:
        terrain = load_map(source,map_number)
        palette = files['COLOR1.PAL'].read_bytes()
        for sample in specification['samples']:
            name = sample['image']
            if Path(name).name != name or not name.startswith('observed_') or not name.endswith('.png'):
                raise ValueError('Unsafe observation filename')
            pixels = decode_frame(files[sample['spr']].read_bytes(),sample['frame'])
            image = composite(terrain,pixels,palette,files[sample['table']].read_bytes(),sample)
            image.save(destination/name)
            result['samples'].append(sample)
        result['status']='ready'
    (destination/'observations.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    return result
