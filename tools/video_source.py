#!/usr/bin/env python3
"""Convert shipped Smacker movies to Godot-supported Ogg Theora/Vorbis.

No resizing, frame-rate override, palette override, or audio normalization.
Theora/Vorbis are lossy: source/output hashes and encoder settings stay explicit.
"""
import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects
from music_source import checked_fixup, smacker_audio

MOVIES = [('INTRO.SMK', 'intro.ogv', 'startup_buka', 0x15714, 0x9932),
          ('FROM.SMK', 'authors.ogv', 'authors_video', 0x1c957, 0xa041)]


def smk_header(data):
    tracks = smacker_audio(data)
    width, height, frames, delay, flags = struct.unpack_from('<IIIiI', data, 4)
    if not (0 < width <= 8192 and 0 < height <= 8192 and 0 < frames <= 1000000) or delay == 0:
        raise ValueError('Invalid or unsupported Smacker dimensions/frame timing')
    period = Fraction(delay, 1000) if delay > 0 else Fraction(-delay, 100000)
    return dict(version=data[:4].decode('ascii'), width=width, height=height, frames=frames,
                raw_frame_delay=delay, raw_flags=flags, ring_frame=bool(flags & 1),
                frame_rate=str(1 / period), duration_seconds=float(frames * period), audio_tracks=tracks)


def probe(path, ffprobe='ffprobe'):
    command = [ffprobe, '-v', 'error', '-count_frames', '-show_entries',
               'stream=index,codec_name,codec_type,width,height,r_frame_rate,nb_read_frames,sample_rate,channels:format=duration',
               '-of', 'json', str(path)]
    return json.loads(subprocess.check_output(command, stderr=subprocess.PIPE))


def conversion_command(source, output, ffmpeg='ffmpeg'):
    return [ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-i', str(source),
            '-map', '0:v:0', '-map', '0:a:0?', '-map_metadata', '-1', '-map_chapters', '-1',
            '-c:v', 'libtheora', '-q:v', '8', '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough',
            '-c:a', 'libvorbis', '-q:a', '6', '-threads', '1',
            '-fflags', '+bitexact', '-flags:v', '+bitexact', '-flags:a', '+bitexact', str(output)]


def theora_frames(data):
    """Count presentation frames from the final granule, including duplicate frames.

    libtheora emits empty duplicate packets which ffprobe's nb_read_frames omits.
    For Theora >=3.2.1: frame count = keyframe + delta in the EOS granule.
    """
    at = 0
    serial = None
    shift = None
    last = None
    ended = False
    while at < len(data):
        if at + 27 > len(data) or data[at:at + 5] != b'OggS\0': raise ValueError('Invalid Ogg page')
        granule, stream_id = struct.unpack_from('<QI', data, at + 6)
        segments = data[at + 26]
        payload = at + 27 + segments
        if payload > len(data): raise ValueError('Truncated Ogg lacing')
        stop = payload + sum(data[at + 27:payload])
        if stop > len(data): raise ValueError('Truncated Ogg payload')
        if data[at + 5] & 2 and data[payload:payload + 7] == b'\x80theora':
            packet = data[payload:stop]
            if serial is not None or len(packet) < 42 or tuple(packet[7:10]) < (3, 2, 1):
                raise ValueError('Unsupported Theora identification header')
            serial = stream_id
            shift = ((packet[40] & 3) << 3) | (packet[41] >> 5)
        if stream_id == serial:
            if granule != 0xffffffffffffffff: last = granule
            if data[at + 5] & 4: ended = True
        at = stop
    if serial is None or last is None or not ended: raise ValueError('Missing Theora EOS granule')
    return (last >> shift) + (last & ((1 << shift) - 1))


def verify_video(header, info, output=False, presentation_frames=None):
    videos = [s for s in info['streams'] if s['codec_type'] == 'video']
    if len(videos) != 1: raise ValueError('Expected exactly one video track')
    video = videos[0]
    codec = 'theora' if output else 'smackvideo'
    if video['codec_name'] != codec or (video['width'], video['height']) != (header['width'], header['height']):
        raise ValueError('Video codec/dimensions changed unexpectedly')
    if Fraction(video['r_frame_rate']) != Fraction(header['frame_rate']):
        raise ValueError('Frame rate changed during conversion')
    frames = presentation_frames if output else int(video['nb_read_frames'])
    if frames != header['frames']:
        raise ValueError('Decoded frame count differs from Smacker header')
    if not 0 < int(video['nb_read_frames']) <= header['frames']:
        raise ValueError('Invalid decoded packet-frame count')
    audio = [s for s in info['streams'] if s['codec_type'] == 'audio']
    expected = header['audio_tracks'][:1]
    if len(audio) != len(expected): raise ValueError('Audio track disappeared or was invented')
    if audio:
        track = audio[0]
        if track['codec_name'] != ('vorbis' if output else 'smackaudio') or int(track['sample_rate']) != expected[0]['sample_rate'] or track['channels'] != expected[0]['channels']:
            raise ValueError('Audio codec/rate/channel count changed unexpectedly')
    if abs(float(info['format']['duration']) - header['duration_seconds']) > float(1 / Fraction(header['frame_rate'])):
        raise ValueError('Video duration changed by more than one frame')


def source_evidence(game):
    raw = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(raw).hexdigest() != EXE_SHA256: raise ValueError('Unknown original executable version')
    le, objects = read_le_objects(raw, INNER_MZ)
    code = objects[0]['payload']
    evidence = []
    for source, _, _, operand, name_at in MOVIES:
        checked_fixup(raw, le, operand, 3, name_at)
        if code[operand - 1] != 0xb8 or struct.unpack_from('<I', code, operand)[0] != name_at:
            raise ValueError('Expected video-name immediate')
        if objects[2]['payload'][name_at:name_at + len(source) + 1].lower() != source.lower().encode() + b'\0':
            raise ValueError('Video resource name changed')
        evidence.append(dict(source=source, le1_operand=operand, target_object=3, target_offset=name_at))
    return evidence


def playback_profiles(game):
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    code = objects[0]['payload']
    profiles = {}
    for name, lo, hi in [('INTRO.SMK', 0x15712, 0x1576a), ('FROM.SMK', 0x1c93e, 0x1c9ad)]:
        setup = code[lo:hi]
        def store(at, byte=False):
            opcode = bytes([0xc6 if byte else 0xc7, 5]) + struct.pack('<I', at)
            if setup.count(opcode) != 1: raise ValueError('Video setup field is no longer unique')
            offset = setup.index(opcode) + len(opcode)
            return setup[offset] if byte else struct.unpack_from('<I', setup, offset)[0]
        signed = lambda n: n if n < 0x80000000 else n - 0x100000000
        profiles[name] = dict(setup_entry=lo, requested_position=[signed(store(0x9a84)), signed(store(0x9a88))],
                              canvas_width=store(0x9a90), canvas_height=store(0x9a94),
                              skip_on_key=bool(store(0x9a9c, byte=True) & 1), repeat_count=store(0x9a9d))
    return profiles


def export(game, output, ffmpeg='ffmpeg', ffprobe='ffprobe'):
    for executable in [ffmpeg, ffprobe]:
        if shutil.which(executable) is None: raise ValueError(f'Missing required developer tool: {executable}')
    evidence = source_evidence(game)
    profiles = playback_profiles(game)
    output.mkdir(parents=True, exist_ok=True)
    records = []
    for name, filename, role, _, _ in MOVIES:
        source = game / name
        raw = source.read_bytes()
        header = smk_header(raw)
        playback = profiles[name].copy()
        x, y = playback['requested_position']
        playback['display_rect'] = [(playback['canvas_width'] - header['width']) // 2 if x == -1 else x,
                                    (playback['canvas_height'] - header['height']) // 2 if y == -1 else y,
                                    header['width'], header['height']]
        if header['ring_frame']: raise ValueError('Ring-frame movies need a separately verified frame-count policy')
        original = probe(source, ffprobe)
        verify_video(header, original)
        destination = output / filename
        temporary = output / (filename + '.tmp.ogv')
        try:
            subprocess.run(conversion_command(source, temporary, ffmpeg), check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            converted = probe(temporary, ffprobe)
            presentation_frames = theora_frames(temporary.read_bytes())
            verify_video(header, converted, output=True, presentation_frames=presentation_frames)
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
        reference_frame = min(header['frames'] - 1, round(5 * Fraction(header['frame_rate'])))
        reference_name = destination.stem + '_reference.png'
        subprocess.run([ffmpeg, '-v', 'error', '-y', '-i', str(source), '-vf', f'select=eq(n\\,{reference_frame})',
                        '-frames:v', '1', str(output / reference_name)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        records.append(dict(source=name, source_bytes=len(raw), source_sha256=hashlib.sha256(raw).hexdigest(),
                            file=filename, role=role, header=header, source_probe=original, output_probe=converted,
                            presentation_frames=presentation_frames,
                            playback=playback,
                            reference=dict(frame=reference_frame, time_seconds=float(reference_frame / Fraction(header['frame_rate'])),
                                           file=reference_name, sha256=hashlib.sha256((output / reference_name).read_bytes()).hexdigest()),
                            output_bytes=destination.stat().st_size, output_sha256=hashlib.sha256(destination.read_bytes()).hexdigest()))
    manifest = dict(format_version=1, executable_sha256=EXE_SHA256, source_pointers=evidence,
                    encoder=dict(video='libtheora', video_quality=8, pixel_format='yuv420p',
                                 audio='libvorbis', audio_quality=6, lossy=True, resized=False, palette_override=False,
                                 version=subprocess.check_output([ffmpeg, '-version'], text=True).splitlines()[0]), videos=records)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/video'))
    parser.add_argument('--ffmpeg', default='ffmpeg'); parser.add_argument('--ffprobe', default='ffprobe')
    args = parser.parse_args()
    result = export(args.game, args.output, args.ffmpeg, args.ffprobe)
    print(f'Exported {len(result["videos"])} Ogg Theora movies with original frame counts/rates')
