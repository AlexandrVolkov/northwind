#!/usr/bin/env python3
"""Export Northwind FX banks as lossless unsigned 8-bit mono WAVs.

The two offset tables and 11025 Hz driver setting are recovered from LE:1;
see docs/sound.md. No executable or original bank is copied to the output.
"""
import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re
import struct
import wave

SAMPLE_RATE = 11025
BANKS = ('WORK.FX', 'MENU.FX', 'MAP_1.FX') + tuple(f'ARMY__0{i}.FX' for i in range(6))


@dataclass(frozen=True)
class Sample:
    sound: int
    variant: int
    group_offset: int
    header_offset: int
    size_word: int
    pcm: bytes

    @property
    def raw_high_byte(self):
        return self.size_word >> 24


def offset_table(data, start, end, label):
    if not 0 <= start <= end <= len(data) or start + 2 > end:
        raise ValueError(f'{label}: truncated count')
    count = struct.unpack_from('<H', data, start)[0]
    header_end = start + 2 + 4 * count
    if header_end > end:
        raise ValueError(f'{label}: truncated offsets')
    offsets = [start + v for v in struct.unpack_from(f'<{count}I', data, start + 2)]
    if not offsets:
        if header_end != end:
            raise ValueError(f'{label}: trailing bytes in empty table')
    elif offsets[0] != header_end or any(a >= b for a, b in zip(offsets, offsets[1:])) or offsets[-1] >= end:
        raise ValueError(f'{label}: noncontiguous header or unordered/out-of-range offsets')
    return offsets


def decode_bank(data):
    """Validate every byte boundary; preserve the on-disk amplitude cache byte."""
    offsets = offset_table(data, 0, len(data), 'bank')
    sounds = []
    for sound, (at, end) in enumerate(zip(offsets, offsets[1:] + [len(data)])):
        if at + 4 > end:
            raise ValueError(f'sound {sound}: truncated size')
        size = struct.unpack_from('<I', data, at)[0]
        if at + 4 + size != end:
            raise ValueError(f'sound {sound}: group size disagrees with next offset')
        variants = offset_table(data, at + 4, end, f'sound {sound}')
        if not 1 <= len(variants) <= 255:  # LE:1 1418D multiplies by a byte, not a word.
            raise ValueError(f'sound {sound}: variant count outside native byte range')
        samples = []
        for variant, (pos, stop) in enumerate(zip(variants, variants[1:] + [end])):
            if pos + 4 > stop:
                raise ValueError(f'sound {sound}/{variant}: truncated sample size')
            size_word = struct.unpack_from('<I', data, pos)[0]
            if pos + 4 + (size_word & 0xffffff) != stop:
                raise ValueError(f'sound {sound}/{variant}: PCM size disagrees with next offset')
            samples.append(Sample(sound, variant, at, pos, size_word, data[pos + 4:stop]))
        sounds.append(samples)
    return sounds


def write_wav(path, pcm):
    with wave.open(str(path), 'wb') as output:
        output.setparams((1, 1, SAMPLE_RATE, 0, 'NONE', 'not compressed'))
        output.writeframes(pcm)
    if len(pcm) & 1:
        # Python's wave omits the RIFF chunk pad; Godot otherwise seeks past EOF.
        data = bytearray(path.read_bytes()) + b'\0'
        struct.pack_into('<I', data, 4, len(data) - 8)
        path.write_bytes(data)


def write_import_settings(path):
    """Godot defaults to lossy QOA; keep PCM and preserve editor-generated UID/deps."""
    sidecar = path.with_suffix('.wav.import')
    text = sidecar.read_text(encoding='utf-8') if sidecar.exists() else '[remap]\n\nimporter="wav"\ntype="AudioStreamWAV"\n\n[params]\n'
    for key, value in {'force/8_bit': 'false', 'force/mono': 'false', 'force/max_rate': 'false',
                       'edit/trim': 'false', 'edit/normalize': 'false', 'edit/loop_mode': '0', 'compress/mode': '0'}.items():
        pattern = rf'(?m)^{re.escape(key)}=.*$'
        if re.search(pattern, text):
            text = re.sub(pattern, f'{key}={value}', text)
        else:
            text += f'{key}={value}\n'
    sidecar.write_text(text, encoding='utf-8')


def mean_level(pcm):
    """LE:1 140B0: mean folded magnitude (negative half is one's complement)."""
    if not pcm:
        raise ValueError('Native mean-level routine requires a nonempty sample')
    return sum((b if b & 0x80 else b ^ 0xff) & 0x7f for b in pcm) // len(pcm)


def export_bank(source, output):
    data = source.read_bytes()
    sounds = decode_bank(data)  # Validate the entire bank before writing any WAV.
    folder = output / source.stem.lower()
    folder.mkdir(parents=True, exist_ok=True)
    records = []
    for variants in sounds:
        samples = []
        for sample in variants:
            name = f'{sample.sound:03d}_{sample.variant:02d}.wav'
            path = folder / name
            write_wav(path, sample.pcm)
            write_import_settings(path)
            samples.append(dict(variant=sample.variant, file=f'{folder.name}/{name}',
                                header_offset=sample.header_offset, pcm_offset=sample.header_offset + 4,
                                raw_size_word=sample.size_word, raw_high_byte=sample.raw_high_byte,
                                mean_level=mean_level(sample.pcm) if sample.pcm else None,
                                frames=len(sample.pcm), duration_seconds=len(sample.pcm) / SAMPLE_RATE,
                                pcm_sha256=hashlib.sha256(sample.pcm).hexdigest(),
                                wav_sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        records.append(dict(sound=variants[0].sound, group_offset=variants[0].group_offset, variants=samples))
    return dict(source=source.name, source_bytes=len(data), source_sha256=hashlib.sha256(data).hexdigest(),
                sound_count=len(sounds), sample_count=sum(map(len, sounds)), sounds=records)


def export(game, output, banks=BANKS):
    if len(set(banks)) != len(banks) or any(bank not in BANKS for bank in banks):
        raise ValueError('Choose each known FX bank at most once')
    output.mkdir(parents=True, exist_ok=True)
    manifest = dict(format_version=1, sample_rate=SAMPLE_RATE, channels=1, bits_per_sample=8,
                    pcm_encoding='unsigned',
                    rate_evidence='LE:1 1331F, 13398, 13411 -> descriptor+3F; 13643 -> driver 0401h',
                    variant_evidence='LE:1 1416B..141B4; index = (random_byte * variant_count) >> 8',
                    banks=[export_bank(game / name, output) for name in banks])
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/sound'))
    parser.add_argument('--bank', action='append', choices=BANKS)
    args = parser.parse_args()
    result = export(args.game, args.output, args.bank or BANKS)
    print(f'Exported {sum(b["sample_count"] for b in result["banks"])} WAVs from {len(result["banks"])} banks')
