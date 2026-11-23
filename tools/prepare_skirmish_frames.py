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
    for index in [2,3,4,5,6,7,8,9,10,11,12,13,14,15,22,23,24]:
        key=('ARMY__02.DAT',index);entry=tables[key[0]]['templates'][index]
        raw=bytes.fromhex(entry['raw_record_hex']);base=struct.unpack_from('<H',raw,0x48)[0]
        wanted.setdefault(key,set()).update(range(base,base+(8*raw[0x4a] if index in [8,10,11] else raw[0x4a] if index==14 else raw[0x4a]+1)))
        if index in [2,3,4,6,9,12,13,15,22,23,24]:   # C88C: a damaged building shows the frames [4A+4B-1, 2·4A+4B-1] of its block, the destruction stages
            wanted[key].update(range(base + raw[0x4a] + raw[0x4b] - 1, base + 2*raw[0x4a] + raw[0x4b]))
        if index in [3,4,6,12,15,23]:   # C88C: the working animation of a building is [base+4A, base+4A+4B)
            wanted[key].update(range(base + raw[0x4a], base + raw[0x4a] + raw[0x4b]))
        if index == 5:   # the fence's shapes: its neighbour table (data/line_neighbors.json) names up to ten of them
            shapes = max(bytes.fromhex(json.loads((Path(__file__).resolve().parents[1]/'data/line_neighbors.json').read_text(encoding='utf-8'))['templates']['5']['pairs_hex'])[0::2]) + 1
            wanted[key].update(range(base, base + shapes*(2*raw[0x4a] + raw[0x4b])))
        if index == 7:   # the bridge's shapes (BB8B): shape · (2·DAT[4A] + DAT[4B]) frames each, six shapes in its neighbour table
            wanted[key].update(range(base, base + 6*(2*raw[0x4a] + raw[0x4b])))
        if index == 10:
            wanted[key].update(range(base + 8*raw[0x4a], base + 8*raw[0x4a] + 8*raw[0x4b]))
        if index in [8,10,11]:
            wanted[key].update(death_frames(raw, base))
    # The horde of Teymur (slot 4, ARMY__01): its soldiers (0 swordsman, 1 archer, 5 moor, 7 rider, 12 arsonist, 19 charmed, 20 shackled) and the tent (18).
    for index in [0,1,5,7,12,19,18]:
        key=('ARMY__01.DAT',index);raw=bytes.fromhex(tables[key[0]]['templates'][index]['raw_record_hex']);base=struct.unpack_from('<H',raw,0x48)[0]
        if index==18:
            wanted.setdefault(key,set()).update(range(base,base+raw[0x4a]+1))
            continue
        wanted.setdefault(key,set()).update(range(base,base+8*raw[0x4a]));wanted[key].update(action_frames(raw,base))
    # The scenery of the first map (slots 6 and 7: jugs, chests, manuscripts, the mage): the frame each object stands on.
    for slot in scenario['slots']:
        if slot['slot_index'] not in (6,7):continue
        for obj in slot['objects']:
            if obj['template_file'] in ('ARMY__03.DAT','ARMY__00.DAT') and obj['template_index'] in (1,9,10,11,13,24,25,26,29,30,33):
                wanted.setdefault((obj['template_file'],obj['template_index']),set()).add(struct.unpack_from('<H',bytes.fromhex(obj['raw_record_hex']),1)[0])
    # Jugs (ARMY__03 1, 30, 33: frames 110h..11Ah, the breaking animation of WRK E4) and the chest (9: 11Ch..127h).
    for jug in (1,30,33):wanted[('ARMY__03.DAT',jug)]=wanted.get(('ARMY__03.DAT',jug),set())|set(range(0x110,0x11b))
    wanted[('ARMY__03.DAT',9)]=wanted.get(('ARMY__03.DAT',9),set())|set(range(0x11c,0x128))
    # Rufin (ARMY__03 13, a mage who fights when attacked): 8 directions of DAT[4A] frames, the swings and the death.
    mage=bytes.fromhex(tables['ARMY__03.DAT']['templates'][13]['raw_record_hex']);mbase=struct.unpack_from('<H',mage,0x48)[0]
    wanted.setdefault(('ARMY__03.DAT',13),set()).update(range(mbase,mbase+8*mage[0x4a]));wanted[('ARMY__03.DAT',13)].update(action_frames(mage,mbase))
    # The birds of the first map (ARMY__05 0): 8 directions of DAT[4A] frames, 32x32.
    bird=bytes.fromhex(tables['ARMY__05.DAT']['templates'][0]['raw_record_hex']);wanted[('ARMY__05.DAT',0)]=set(range(struct.unpack_from('<H',bird,0x48)[0],struct.unpack_from('<H',bird,0x48)[0]+8*bird[0x4a]))
    # The pit trap (ARMY__03 12): 19 opening frames from 289h; its first frame is the closed pit.
    wanted[('ARMY__03.DAT',12)]=set(range(0x289,0x29d))
    # Effects of ARMY__00 (WRK effect types; sizes from the type table of ARMY__00.DAT, the record w,h,u32 handler,flag starts at the 2,2 / 4F3 type 0):
    # the sparks of a strike or of a chopped tree, types 4 and 5 (frames 62h..71h and 7Dh..84h of ARMY__00.SPR).
    eff=files['ARMY__00.DAT'].read_bytes();t0=eff.find(bytes([2,2])+struct.pack('<I',0x4f3))
    for kind,frames in {2:range(0x19,0x39),3:range(0x72,0x7d),4:range(0x62,0x72),5:range(0x7d,0x85),8:range(0xbb,0xbc),10:range(0xbc,0xce),11:range(0x54,0x62),12:range(0x85,0x97),13:range(0x97,0xa9),14:range(0xa9,0xbb),0x11:range(0x19,0x39),0x12:range(0x49,0x54),0x13:range(0xce,0x104),0x14:range(0x104,0x13a),0x15:range(0x13a,0x170),0x1d:range(0x226,0x236),0x18:range(0x18c,0x1a5),0x16:range(0x170,0x17a)}.items():wanted[('ARMY__00.EFF',kind)]=set(frames)   # 3: the blood splash (6C7, 11 frames)
    # ARMY__02 effect type 1, the corpse (161D): 62x62; after the first stage its frames are 235h + 3·variant + stage.
    wanted[('ARMY__02.EFF',1)]=set(range(0x235,0x235+25))
    entries=[];images=[];team_pixels=[];horde_images=[]
    for (filename,index),frames in sorted(wanted.items()):
        if filename=='ARMY__00.EFF':entry={'width':eff[t0+7*index],'height':eff[t0+7*index+1]};sprite_name='ARMY__00.SPR'
        elif filename=='ARMY__02.EFF':entry={'width':62,'height':62};sprite_name='ARMY__02.SPR'
        else:entry=tables[filename]['templates'][index];sprite_name=filename.replace('.DAT','.SPR')
        data=files[sprite_name].read_bytes();hashes[sprite_name]=hashlib.sha256(data).hexdigest()
        for frame in sorted(frames):
            pixels=decode_spr_frame(data,frame)
            if len(pixels)!=entry['width']*entry['height']:raise ValueError(f'Frame size mismatch: {filename}:{index}:{frame}')
            image=Image.new('RGBA',(entry['width'],entry['height']))
            image.putdata([tuple((c&252)|(c>>6) for c in palette[table[v]*3:table[v]*3+3])+(255,) if v>=0 else (0,0,0,0) for v in pixels])
            # The frames of ARMY__01 (the horde) and ARMY__03 (the map's objects) fill a second page: one page of 128 cells cannot hold them under the 16384 px texture limit.
            page=1 if filename in ('ARMY__01.DAT','ARMY__03.DAT','ARMY__05.DAT') else 0;target=horde_images if page else images
            slot=len(target);target.append(image);team_pixels.append(pixels if any(v>=240 for v in pixels) else None)
            entries.append({'template_file':filename,'template_index':index,'frame':frame,'page':page,
                            'x':slot%32*128,'y':slot//32*128,'width':image.width,'height':image.height})
    atlas=Image.new('RGBA',(4096,((len(images)+31)//32)*128))   # 32 columns keep the height under 16384
    for entry,image in zip([e for e in entries if not e['page']],images):atlas.paste(image,(entry['x'],entry['y']))
    atlas.save(output/'battle_frames.png')
    page2=Image.new('RGBA',(4096,((len(horde_images)+31)//32)*128))
    for entry,image in zip([e for e in entries if e['page']],horde_images):page2.paste(image,(entry['x'],entry['y']))
    page2.save(output/'battle_frames_horde.png')
    # The owner's colour: COLOR_NN.TAB, NN = byte 0 of the owner's scenario slot (LE:1 2F79..2F90), differs from COLOR_02 only
    # in the sprite indices 240..255. Frames that use them are repeated, per table, in 64x64 slots (units are at most 62x62).
    team=[(e,v) for e,v in zip(entries,team_pixels) if v and e['width']<=64 and e['height']<=64]
    for slot,(entry,_) in enumerate(team):entry['team_slot']=slot
    # Tables of the sides that play: the player, the enemy and the observed rival (slot 1, SkirmishRules.RivalSlot).
    tables_used=sorted({scenario_slot_color(s) for s in scenario['slots'] if s['slot_index'] in [rules['player_slot'],rules['enemy_slot'],1,4]})
    for number in tables_used:
        recolor=files[f'COLOR_{number:02d}.TAB'].read_bytes();hashes[f'COLOR_{number:02d}.TAB']=hashlib.sha256(recolor).hexdigest()
        sheet=Image.new('RGBA',(4096,((len(team)+63)//64)*64))
        for slot,(entry,pixels) in enumerate(team):
            image=Image.new('RGBA',(entry['width'],entry['height']))
            image.putdata([tuple((c&252)|(c>>6) for c in palette[recolor[v]*3:recolor[v]*3+3])+(255,) if v>=0 else (0,0,0,0) for v in pixels])
            sheet.paste(image,(slot%64*64,slot//64*64))
        sheet.save(output/f'battle_frames_team_{number:02d}.png')
    # The pictures of the items for the inventory window: the map object a dropped item becomes (the class byte +0C of the item table is its ARMY__00 template; its first frame).
    items=json.loads((Path(__file__).resolve().parents[1]/'data/original_items.json').read_text(encoding='utf-8'))['items']
    army0=files['ARMY__00.SPR'].read_bytes();icons=Image.new('RGBA',(62*36,62))
    for item in items:
        cls=int(item['static_hex'][:2],16)
        if not cls:continue
        entry=tables['ARMY__00.DAT']['templates'][cls];pixels=decode_spr_frame(army0,entry['initial_frame'])
        if len(pixels)!=entry['width']*entry['height']:continue
        icon=Image.new('RGBA',(entry['width'],entry['height']));icon.putdata([tuple((c&252)|(c>>6) for c in palette[table[v]*3:table[v]*3+3])+(255,) if v>=0 else (0,0,0,0) for v in pixels])
        icons.paste(icon,(62*item['code']+(62-entry['width'])//2,(62-entry['height'])//2))
    icons.save(output/'item_icons.png')
    (output/'battle_frames.json').write_text(json.dumps({'format_version':1,'source_sha256':hashes,'team_tables':tables_used,'frames':entries},indent=2)+'\n',encoding='utf-8')
    print(f'Prepared {len(images)} frames, {len(team)} with owner colours in {len(tables_used)} tables; playback timing and anchors remain experimental.')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('source',type=Path)
    parser.add_argument('--output',type=Path,default=Path(__file__).resolve().parents[1]/'assets/imported')
    args=parser.parse_args();prepare(args.source,args.output)
