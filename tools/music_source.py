#!/usr/bin/env python3
"""Identify original music/ambience sources and export their scene references.

FX PCM is already exported by sound_source.py. This tool verifies executable
call sites, inventories MIDI-family containers, and records Smacker audio headers.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects
from sound_source import decode_bank

MENU_SITES = [('intro_history', 0x19d7f), ('main_menu', 0x1879c),
              ('handler_19c6e', 0x19cb8), ('handler_1ce9a', 0x1cf40)]
AMBIENT_SITES = [0x148e3, 0x148f8, 0x1490d, 0x14922, 0x14937, 0x1494c]


def sound_call(code, at, target, bx=None):
    """MOV EAX,imm32; optional MOV EBX,imm32; relative CALL (LE:1 offsets)."""
    end = at + (15 if bx is not None else 10)
    if end > len(code) or code[at] != 0xb8:
        raise ValueError('Expected sound-number immediate')
    sound = struct.unpack_from('<I', code, at + 1)[0]
    call = at + 5
    if bx is not None:
        if code[call] != 0xbb or struct.unpack_from('<I', code, call + 1)[0] != bx:
            raise ValueError('Unexpected sound playback mode/repeat count')
        call += 5
    if code[call] != 0xe8 or call + 5 + struct.unpack_from('<i', code, call + 1)[0] != target:
        raise ValueError('Sound call no longer targets the expected dispatcher')
    return sound


def midi_containers(data):
    """Report structurally plausible SMF/XMIDI/CMF headers, not incidental marker text."""
    found = []
    start = 0
    while (at := data.find(b'MThd', start)) >= 0:
        start = at + 1
        if at + 22 <= len(data):
            size, kind, tracks, division = struct.unpack_from('>IHHH', data, at + 4)
            if size == 6 and kind <= 2 and 0 < tracks <= 128 and division and data[at + 14:at + 18] == b'MTrk':
                length = struct.unpack_from('>I', data, at + 18)[0]
                if at + 22 + length <= len(data): found.append(dict(kind='SMF', offset=at))
    start = 0
    while (at := data.find(b'FORM', start)) >= 0:
        start = at + 1
        if at + 12 <= len(data) and data[at + 8:at + 12] in (b'XDIR', b'XMID'):
            size = struct.unpack_from('>I', data, at + 4)[0]
            if 4 <= size <= len(data) - at - 8: found.append(dict(kind='XMIDI', offset=at))
    start = 0
    while (at := data.find(b'CTMF', start)) >= 0:
        start = at + 1
        if at + 40 <= len(data):
            version, instruments, music = struct.unpack_from('<HHH', data, at + 4)
            if version in (0x100, 0x101) and 40 <= instruments < music < len(data) - at:
                found.append(dict(kind='CMF', offset=at))
    return sorted(found, key=lambda r: r['offset'])


def smacker_audio(data):
    if len(data) < 104 or data[:4] not in (b'SMK2', b'SMK4'):
        raise ValueError('Truncated or unsupported Smacker header')
    tracks = []
    for track, rate_word in enumerate(struct.unpack_from('<7I', data, 72)):
        if rate_word & 0x40000000:
            tracks.append(dict(track=track, raw_rate_word=rate_word, sample_rate=rate_word & 0xffffff,
                               channels=2 if rate_word & 0x10000000 else 1,
                               bits_per_sample=16 if rate_word & 0x20000000 else 8,
                               packed=bool(rate_word & 0x80000000)))
    return tracks


def checked_fixup(executable, le, source, target_object, target_offset):
    u32 = lambda at: struct.unpack_from('<I', executable, at)[0]
    pages = le + u32(le + 0x68)
    records = le + u32(le + 0x6c)
    page = source // 4096
    blob = executable[records + u32(pages + page * 4):records + u32(pages + (page + 1) * 4)]
    expected = struct.pack('<BBHBH', 7, 0, source % 4096, target_object, target_offset)
    if blob.count(expected) != 1:
        raise ValueError(f'Unexpected resource-pointer fixup at {source:#x}')


def generate(game):
    executable = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(executable).hexdigest() != EXE_SHA256:
        raise ValueError('Unknown original executable version')
    le, objects = read_le_objects(executable, INNER_MZ)
    code = objects[0]['payload']
    for args in [(0x18c4c, 2, 0x294), (0x147a5, 3, 0x6bc6)]: checked_fixup(executable, le, *args)
    if objects[1]['payload'][0x294:0x29c] != b'menu.fx\0' or objects[2]['payload'][0x6bc6:0x6bce] != b'MAP_1.FX':
        raise ValueError('Unexpected original audio bank names')
    if code[0x1cc0b:0x1cc14].hex() != 'bb0100000033c933d2':
        raise ValueError('Menu loops no longer use BX=1 and nonpositional mode')
    profiles = [dict(role=role, bank='MENU.FX', sound=sound_call(code, at, 0x1cbf7),
                     loop_forever=True, mixer_mode=1, source_call=at + 5) for role, at in MENU_SITES]
    ambient = [dict(bank='MAP_1.FX', sound=sound_call(code, at, 0x1416b, bx=3),
                    loop_forever=True, mixer_mode=3, source_call=at + 10) for at in AMBIENT_SITES]
    bank_info = []
    for name in ['MENU.FX', 'MAP_1.FX']:
        raw = (game / name).read_bytes()
        sounds = decode_bank(raw)
        bank_info.append(dict(source=name, source_sha256=hashlib.sha256(raw).hexdigest(),
                              sounds=[dict(sound=i, variants=[dict(file=f'{Path(name).stem.lower()}/{i:03d}_{j:02d}.wav',
                                          frames=len(sample.pcm), pcm_sha256=hashlib.sha256(sample.pcm).hexdigest())
                                          for j, sample in enumerate(variants)]) for i, variants in enumerate(sounds)]))
    for profile in profiles + ambient:
        bank = next(b for b in bank_info if b['source'] == profile['bank'])
        if profile['sound'] >= len(bank['sounds']): raise ValueError('Scene references a missing sound')

    inventory = hashlib.sha256()
    candidates = []
    count = 0
    for path in sorted(game.rglob('*')):
        if not path.is_file(): continue
        raw = path.read_bytes()
        name = path.relative_to(game).as_posix()
        checksum = hashlib.sha256(raw).hexdigest()
        inventory.update(f'{name}\0{len(raw)}\0{checksum}\n'.encode('utf-8'))
        hits = midi_containers(raw)
        if hits or path.suffix.lower() in ('.mid', '.midi', '.xmi', '.cmf', '.mus', '.mdi', '.dig'):
            candidates.append(dict(source=name, source_sha256=checksum, headers=hits))
        count += 1
    videos = []
    for name in ['INTRO.SMK', 'FROM.SMK']:
        raw = (game / name).read_bytes()
        videos.append(dict(source=name, source_sha256=hashlib.sha256(raw).hexdigest(), audio_tracks=smacker_audio(raw)))
    xmi = objects[1]['payload'][0x26c:0x278]
    if xmi != b'xmi_lib.mus\0': raise ValueError('Unexpected legacy MUS name')
    return dict(format_version=1, executable_sha256=EXE_SHA256, sample_rate=11025,
                menu_profiles=profiles, ambient_channels=ambient, banks=bank_info,
                legacy_mus_string=dict(name='xmi_lib.mus', le_object=2, offset=0x26c,
                                       present_in_game=any(p.name.lower() == 'xmi_lib.mus' for p in game.rglob('*')),
                                       direct_u32_code_references=code.count(struct.pack('<I', 0x26c))),
                inventory=dict(file_count=count, sha256=inventory.hexdigest(), midi_family_candidates=candidates), videos=videos)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('data/original_audio_scenes.json'))
    args = parser.parse_args()
    result = generate(args.game)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Audio scenes: {len(result["menu_profiles"])} menu profiles, {len(result["ambient_channels"])} ambient loops; scanned {result["inventory"]["file_count"]} files')
