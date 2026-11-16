#!/usr/bin/env python3
"""Prepare a separate, reproducible DOSBox install. Never patch the game EXE."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil


def dosbox_path(path: Path) -> str:
    text = str(path)
    if any(character in text for character in ('"', '\r', '\n')):
        raise ValueError('DOSBox paths cannot contain quotes or line breaks')
    return f'"{text}"'


def prepare(game: Path, iso: Path, output: Path, drive: str = 'D') -> dict:
    game, iso, output = game.resolve(), iso.resolve(), output.resolve()
    drive = drive.upper()
    if len(drive) != 1 or not 'D' <= drive <= 'Z':
        raise ValueError('CD drive must be a single letter D through Z')
    if not game.is_dir() or not iso.is_file():
        raise ValueError('Expected an extracted game directory and an existing ISO file')
    if output == game or game in output.parents:
        raise ValueError('Output must be outside the source game directory')
    if output.exists():
        raise ValueError('Output already exists; choose a new directory to preserve it')
    names = {path.name.upper(): path for path in game.iterdir() if path.is_file()}
    if 'NORWIND.EXE' not in names:
        raise ValueError('Game directory has no NORWIND.EXE')
    # Validate config strings before any writes.
    game_mount, image_mount = dosbox_path(output / 'game'), dosbox_path(iso)
    original_exe = names['NORWIND.EXE'].read_bytes()
    original_cd = names['CD'].read_bytes() if 'CD' in names else None
    output.mkdir(parents=True)
    shutil.copytree(game, output / 'game')
    cd_name = names['CD'].name if 'CD' in names else 'CD'
    (output / 'game' / cd_name).write_bytes(drive.encode('ascii'))
    if original_cd is not None:
        (output / 'original-CD.bin').write_bytes(original_cd)
    copied_exe = (output / 'game' / names['NORWIND.EXE'].name).read_bytes()
    if copied_exe != original_exe:
        raise ValueError('Copied executable differs from original')
    config = f'''[sdl]
fullscreen=false
output=surface
[dosbox]
machine=svga_s3
memsize=64
[cpu]
core=dynamic
cycles=fixed 30000
[mixer]
nosound=true
[autoexec]
mount c {game_mount}
imgmount {drive.lower()} {image_mount} -t iso
c:
norwind.exe -nosound -noearth
'''
    (output / 'dosbox.conf').write_text(config, encoding='utf-8')
    iso_hash = hashlib.sha256()
    with iso.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            iso_hash.update(chunk)
    iso_digest = iso_hash.hexdigest()
    report = {
        'format_version': 1, 'cd_drive': drive,
        'original_cd_hex': original_cd.hex() if original_cd is not None else None,
        'configured_cd_hex': drive.encode('ascii').hex(),
        'executable_sha256': hashlib.sha256(original_exe).hexdigest(),
        'executable_unchanged': copied_exe == original_exe,
        'iso_sha256': iso_digest,
        'game_source': str(game), 'iso_source': str(iso),
        'notes': ['Only CD in the staged copy is configured.',
                  'Original CD bytes are backed up outside the staged game.',
                  'Audio disabled for the baseline; no timing or gameplay parity established.'],
    }
    (output / 'launch-manifest.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--iso', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--drive', default='D')
    args = parser.parse_args()
    prepare(args.game, args.iso, args.output, args.drive)
    print(f'Prepared {args.output.resolve()}')
    print(f'Run: dosbox -conf {dosbox_path(args.output.resolve() / "dosbox.conf")}')


if __name__ == '__main__':
    main()
