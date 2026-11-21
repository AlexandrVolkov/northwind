#!/usr/bin/env python3
"""Prepare strict-sized original frames used by the playable experimental mission."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image
from import_game import decode_spr_frame


def action_frames(raw, base):
    """Swing frames (9406: base + 8*DAT[4A] + direction*DAT[4B]) and death frames."""
    return set(range(base + 8*raw[0x4a], base + 8*raw[0x4a] + 8*raw[0x4b])) | death_frames(raw, base)


def death_frames(raw, base):
    """A4DF: base + 8*(DAT[4A]+DAT[4B]) + 4*sum(DAT[4F..52]) frames for four directions and hit kinds."""
    start = base + 8*((raw[0x4a] + raw[0x4b]) & 255)
    return set(range(start, start + 4*sum(raw[0x4f:0x53])))


def scenario_slot_color(slot):
    return bytes.fromhex(slot['raw_record_hex'])[0]


def prepare(source, output):
    tables=json.loads((output/'templates.json').read_text(encoding='utf-8'))['armies']
    scenario=json.loads((output/'scenario.json').read_text(encoding='utf-8'))
    rules=json.loads((Path(__file__).resolve().parents[1]/'data/skirmish.json').read_text(encoding='utf-8'))
    files={p.name.upper():p for p in source.iterdir() if p.is_file()}
    palette=files['COLOR1.PAL'].read_bytes();table=files['COLOR_02.TAB'].read_bytes()
    if len(palette)!=768 or len(table)!=256:raise ValueError('Invalid palette or color table')
    wanted={};hashes={name:hashlib.sha256(files[name].read_bytes()).hexdigest() for name in ['COLOR1.PAL','COLOR_02.TAB']}
    for slot in scenario['slots']:
        if slot['slot_index'] not in [rules['player_slot'],rules['enemy_slot']]:continue
        for obj in slot['objects']:
            if obj['template_name'] not in rules['units']:continue
            key=(obj['template_file'],obj['template_index'])
            entry=tables[key[0]]['templates'][key[1]];raw=bytes.fromhex(entry['raw_record_hex'])
            base=struct.unpack_from('<H',raw,0x48)[0]
            frames=wanted.setdefault(key,set())
            frames.add(struct.unpack_from('<H',bytes.fromhex(obj['raw_record_hex']),1)[0])
            frames.add(base)
            if rules['units'][obj['template_name']]['damage']:
                frames.update(range(base,base+8*raw[0x4a]))
                frames.update(action_frames(raw, base))
    # Runtime economy objects are not initially present in the player's source slot.
    # 7, 12, 15, 22, 23, 24: buildings the enemy AI orders (227C7; 7 is the bridge of order mode 10); 16, 17, 18, 21, 26: units their producers make.
    for index in [16,17,18,21,26]:
        key=('ARMY__02.DAT',index);raw=bytes.fromhex(tables[key[0]]['templates'][index]['raw_record_hex'])
        base=struct.unpack_from('<H',raw,0x48)[0]
        wanted.setdefault(key,set()).update(range(base,base+8*raw[0x4a]))
        wanted[key].update(action_frames(raw, base)); wanted[key].update(death_frames(raw, base))
    for index in [3,4,6,7,9,10,11,12,15,22,23,24]:
        key=('ARMY__02.DAT',index);entry=tables[key[0]]['templates'][index]
        raw=bytes.fromhex(entry['raw_record_hex']);base=struct.unpack_from('<H',raw,0x48)[0]
        wanted.setdefault(key,set()).update(range(base,base+(8*raw[0x4a] if index in [10,11] else raw[0x4a]+1)))
        if index == 10:
            wanted[key].update(range(base + 8*raw[0x4a], base + 8*raw[0x4a] + 8*raw[0x4b]))
        if index in [10,11]:
            wanted[key].update(death_frames(raw, base))
    entries=[];images=[];team_pixels=[]
    for (filename,index),frames in sorted(wanted.items()):
        entry=tables[filename]['templates'][index];sprite_name=filename.replace('.DAT','.SPR')
        data=files[sprite_name].read_bytes();hashes[sprite_name]=hashlib.sha256(data).hexdigest()
        for frame in sorted(frames):
            pixels=decode_spr_frame(data,frame)
            if len(pixels)!=entry['width']*entry['height']:raise ValueError(f'Frame size mismatch: {filename}:{index}:{frame}')
            image=Image.new('RGBA',(entry['width'],entry['height']))
            image.putdata([tuple((c&252)|(c>>6) for c in palette[table[v]*3:table[v]*3+3])+(255,) if v>=0 else (0,0,0,0) for v in pixels])
            slot=len(images);images.append(image);team_pixels.append(pixels if any(v>=240 for v in pixels) else None)
            entries.append({'template_file':filename,'template_index':index,'frame':frame,
                            'x':slot%32*128,'y':slot//32*128,'width':image.width,'height':image.height})
    atlas=Image.new('RGBA',(4096,((len(images)+31)//32)*128))   # 32 columns keep the height under 16384
    for entry,image in zip(entries,images):atlas.paste(image,(entry['x'],entry['y']))
    atlas.save(output/'battle_frames.png')
    # The owner's colour: COLOR_NN.TAB, NN = byte 0 of the owner's scenario slot (LE:1 2F79..2F90), differs from COLOR_02 only
    # in the sprite indices 240..255. Frames that use them are repeated, per table, in 64x64 slots (units are at most 62x62).
    team=[(e,v) for e,v in zip(entries,team_pixels) if v and e['width']<=64 and e['height']<=64]
    for slot,(entry,_) in enumerate(team):entry['team_slot']=slot
    # Tables of the sides that play: the player, the enemy and the observed rival (slot 1, SkirmishRules.RivalSlot).
    tables_used=sorted({scenario_slot_color(s) for s in scenario['slots'] if s['slot_index'] in [rules['player_slot'],rules['enemy_slot'],1]})
    for number in tables_used:
        recolor=files[f'COLOR_{number:02d}.TAB'].read_bytes();hashes[f'COLOR_{number:02d}.TAB']=hashlib.sha256(recolor).hexdigest()
        sheet=Image.new('RGBA',(4096,((len(team)+63)//64)*64))
        for slot,(entry,pixels) in enumerate(team):
            image=Image.new('RGBA',(entry['width'],entry['height']))
            image.putdata([tuple((c&252)|(c>>6) for c in palette[recolor[v]*3:recolor[v]*3+3])+(255,) if v>=0 else (0,0,0,0) for v in pixels])
            sheet.paste(image,(slot%64*64,slot//64*64))
        sheet.save(output/f'battle_frames_team_{number:02d}.png')
    (output/'battle_frames.json').write_text(json.dumps({'format_version':1,'source_sha256':hashes,'team_tables':tables_used,'frames':entries},indent=2)+'\n',encoding='utf-8')
    print(f'Prepared {len(images)} frames, {len(team)} with owner colours in {len(tables_used)} tables; playback timing and anchors remain experimental.')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('source',type=Path)
    parser.add_argument('--output',type=Path,default=Path(__file__).resolve().parents[1]/'assets/imported')
    args=parser.parse_args();prepare(args.source,args.output)
