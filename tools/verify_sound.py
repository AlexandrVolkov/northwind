#!/usr/bin/env python3
"""Run native FX selection, level preprocessing, PCM gain and driver defaults.

Fixtures contain input parameters/hashes only. Hardware IRQ/DMA and the RNG
itself are outside the oracle: RNG returns a controlled byte at LE:1 9E69.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_ESI, UC_X86_REG_ESP, UC_X86_REG_EIP
from analyze_executable import analyze
from sound_source import BANKS, SAMPLE_RATE, decode_bank, mean_level
from verify_occupancy import OccupancyOracle


class SoundOracle(OccupancyOracle):
    BANK = 0x1000000

    def __init__(self, code, assignments):
        super().__init__(code, assignments)
        self.cpu.mem_map(self.BANK, 0x200000)
        self.random_byte = 0
        self.selected = False

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if self.mode == 'select':
            if at == 0x9e69:
                cpu.reg_write(UC_X86_REG_EAX, self.random_byte)
                sp = cpu.reg_read(UC_X86_REG_ESP)
                cpu.reg_write(UC_X86_REG_EIP, struct.unpack('<I', cpu.mem_read(sp, 4))[0])
                cpu.reg_write(UC_X86_REG_ESP, sp + 4)
                return
            if at == 0x141a0:
                self.selected = True
                cpu.emu_stop()
                return
            allowed = [(0x1416b, 0x141b5)]
        else:
            allowed = {'level': [(0x140b0, 0x140db)], 'prepare': [(0x1405b, 0x140db)],
                       'gain': [(0x13f50, 0x13f93)],
                       'defaults': [(0x13301, 0x1346c)]}[self.mode]
        if not any(lo <= at < hi for lo, hi in allowed):
            raise ValueError(f'Unexpected sound control-flow escape: {address:#x} ({self.mode})')

    def run(self, mode, entry):
        self.mode = mode
        self.start(self.CODE + entry)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError(f'Native sound {mode} failed to return')

    def select(self, sound, value, enabled=True):
        self.mode = 'select'
        self.random_byte = value
        self.selected = False
        self.cpu.mem_write(0x9091, bytes([int(enabled)]))
        self.cpu.reg_write(UC_X86_REG_EAX, sound)
        self.cpu.reg_write(UC_X86_REG_ESI, self.BANK)
        self.start(self.CODE + 0x1416b)
        if not self.selected:
            if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
                raise ValueError('Selection neither returned nor reached its boundary')
            return None
        return (self.cpu.reg_read(UC_X86_REG_ESI) - self.BANK,
                self.cpu.reg_read(UC_X86_REG_EAX))


def generate(game):
    report, code = analyze(game)
    o = SoundOracle(code, report['fs_assignments'])
    result = dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'])
    defaults = []
    for entry in [0x13301, 0x1337a, 0x133f3]:
        o.cpu.mem_write(o.DAT, bytes(0x50))
        o.cpu.reg_write(UC_X86_REG_EAX, o.DAT)
        o.run('defaults', entry)
        rate = struct.unpack('<H', o.cpu.mem_read(o.DAT + 0x3f, 2))[0]
        if rate != SAMPLE_RATE:
            raise ValueError('Unexpected native FX playback rate')
        defaults.append(dict(entry=entry, sample_rate=rate, stereo=o.cpu.mem_read(o.DAT + 0x3d, 1)[0]))
    result['driver_defaults'] = defaults
    digest = hashlib.sha256()
    counts = [1, 2, 3, 4, 5, 7, 16, 255]
    for count in counts:
        # One sound; every sample contains one synthetic byte. Independent of the converter.
        group = struct.pack('<H', count) + struct.pack(f'<{count}I', *(2 + count * 4 + i * 5 for i in range(count)))
        group += b''.join(struct.pack('<I', 1) + bytes([i]) for i in range(count))
        data = struct.pack('<HII', 1, 6, len(group)) + group
        o.cpu.mem_write(o.BANK, data)
        for value in range(256):
            offset, size_word = o.select(0, value)
            variant, tail = divmod(offset - (10 + 2 + count * 4 + 4), 5)
            if tail or size_word != 1:
                raise ValueError('Unexpected synthetic selection pointer or size')
            digest.update(bytes([variant]))
        if o.select(1, 0) is not None or o.select(0, 0, enabled=False) is not None:
            raise ValueError('Native disabled/out-of-range selection did not return')
    result['selection'] = dict(variant_counts=counts, case_count=len(counts) * 256, sha256=digest.hexdigest())

    banks = []
    for name in BANKS:
        data = (game / name).read_bytes()
        sounds = decode_bank(data)
        o.cpu.mem_write(o.BANK, data)
        digest = hashlib.sha256()
        for sound, variants in enumerate(sounds):
            for value in range(256):
                actual = o.select(sound, value)
                sample = variants[(value * len(variants)) >> 8]
                if actual != (sample.header_offset + 4, sample.size_word):
                    raise ValueError(f'Converter disagrees with native selection: {name}/{sound}/{value}')
                digest.update(struct.pack('<II', *actual))
        banks.append(dict(source=name, source_sha256=hashlib.sha256(data).hexdigest(),
                          case_count=len(sounds) * 256, sha256=digest.hexdigest()))
    result['banks'] = banks

    digest = hashlib.sha256()
    for sample in range(256):
        for gain in range(256):
            o.cpu.reg_write(UC_X86_REG_EAX, sample | gain << 8)
            o.run('gain', 0x13f50)
            digest.update(bytes([o.cpu.reg_read(UC_X86_REG_EAX) & 255]))
    result['gain'] = dict(case_count=65536, sha256=digest.hexdigest())

    lengths = [1, 2, 3, 7, 31, 257, 512]
    digest = hashlib.sha256()
    count = 0
    for length in lengths:
        for kind in ['silence', 'zero', 'full', 'ramp']:
            pcm = bytes([128] * length) if kind == 'silence' else bytes(length) if kind == 'zero' else bytes([255] * length) if kind == 'full' else bytes(i % 256 for i in range(length))
            o.cpu.mem_write(o.BANK, pcm)
            o.cpu.reg_write(UC_X86_REG_ESI, o.BANK)
            o.cpu.reg_write(UC_X86_REG_ECX, len(pcm))
            o.run('level', 0x140b0)
            level = o.cpu.reg_read(UC_X86_REG_EAX)
            if level != mean_level(pcm):
                raise ValueError('Mean level disagrees with native preprocessing')
            digest.update(bytes([level]))
            count += 1
    result['mean_level'] = dict(lengths=lengths, kinds=['silence', 'zero', 'full', 'ramp'], case_count=count, sha256=digest.hexdigest())
    # The preprocessor writes BEFORE the PCM, in the size word's high byte, not into the sample.
    pcm = bytes(range(64))
    group = struct.pack('<HI', 1, 6) + struct.pack('<I', len(pcm)) + pcm
    data = struct.pack('<HII', 1, 6, len(group)) + group
    o.cpu.mem_write(o.BANK, data)
    o.cpu.reg_write(UC_X86_REG_ESI, o.BANK)
    o.run('prepare', 0x1405b)
    expected = bytearray(data)
    expected[19] = mean_level(pcm)
    if bytes(o.cpu.mem_read(o.BANK, len(data))) != expected:
        raise ValueError('Preprocessing changed bytes other than amplitude cache')
    result['preprocessor_verified'] = True
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Native sound: {sum(b["case_count"] for b in result["banks"])} bank selections, {result["gain"]["case_count"]} gains')
