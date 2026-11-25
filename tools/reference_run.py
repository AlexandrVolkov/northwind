#!/usr/bin/env python3
"""Headless runtime reference: the original NORWIND.EXE in DOSBox-X under Xvfb, driven with xdotool, observed with screenshots and by reading the emulated memory from /proc/<pid>/mem.

  python3 tools/reference_run.py start  --conf /opt/game/run1/dosbox.conf [--display :77]
  python3 tools/reference_run.py shot   --out shot.png [--display :77]
  python3 tools/reference_run.py click  X Y            (mouse in the 800x600 Xvfb screen)
  python3 tools/reference_run.py key    KEY...         (xdotool key names)
  python3 tools/reference_run.py peek   OFFSET [LEN]   (bytes of the game data segment: DS + OFFSET)
  python3 tools/reference_run.py stop

The game data segment is found by its signature: the string "D:\\7bitlabs" lives at DS + 0x1EE4 (docs/original-launch.md); the emulated RAM is the 64 MB anonymous region of DOSBox-X,
so the emulated linear address L is at region_start + L. [6AA] then holds the linear address of the object pool, and so on. No original bytes are stored by this tool.
"""
import argparse
import os
import re
import struct
import subprocess
import sys
import time

SIGNATURE = b'D:\\7bitlabs'
SIGNATURE_OFFSET = 0x1EE4


def run(cmd, **kw): return subprocess.run(cmd, check=False, **kw)


def pid():
    out = subprocess.run(['pgrep', '-n', 'dosbox-x'], capture_output=True, text=True).stdout.strip()
    if not out: sys.exit('dosbox-x is not running')
    return int(out)


def regions(p):
    for line in open(f'/proc/{p}/maps'):
        m = re.match(r'([0-9a-f]+)-([0-9a-f]+) (\S+) ', line)
        a, b, perm = int(m.group(1), 16), int(m.group(2), 16), m.group(3)
        if 'r' in perm and 'w' in perm and b - a >= 0x1000000: yield a, b


def find_ds(p):
    """(region_start, ds_host_address) of the running game, or exit."""
    with open(f'/proc/{p}/mem', 'rb', 0) as mem:
        for a, b in regions(p):
            try: mem.seek(a); data = mem.read(b - a)
            except OSError: continue
            i = data.find(SIGNATURE)
            if i >= 0: return a, a + i - SIGNATURE_OFFSET
    sys.exit('the data segment signature was not found (is the game past its start-up?)')


def peek(offset, length=16):
    p = pid(); region, ds = find_ds(p)
    with open(f'/proc/{p}/mem', 'rb', 0) as mem: mem.seek(ds + offset); return mem.read(length)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('command', choices=['start', 'shot', 'click', 'key', 'peek', 'stop'])
    ap.add_argument('args', nargs='*')
    ap.add_argument('--conf', default='/opt/game/run1/dosbox.conf'); ap.add_argument('--display', default=':77'); ap.add_argument('--out', default='shot.png')
    a = ap.parse_args()
    env = dict(os.environ, DISPLAY=a.display)
    if a.command == 'start':
        subprocess.Popen(['Xvfb', a.display, '-screen', '0', '800x600x24'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL); time.sleep(2)
        subprocess.Popen(['dosbox-x', '-conf', a.conf, '-fastlaunch'], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL); time.sleep(20)
    elif a.command == 'shot': run(['import', '-window', 'root', a.out], env=env)
    elif a.command == 'click': run(['xdotool', 'mousemove', a.args[0], a.args[1]], env=env); time.sleep(0.5); run(['xdotool', 'click', '1'], env=env)
    elif a.command == 'key': run(['xdotool', 'key', *a.args], env=env)
    elif a.command == 'peek': print(peek(int(a.args[0], 0), int(a.args[1], 0) if len(a.args) > 1 else 16).hex())
    else: run(['pkill', 'dosbox-x']); run(['pkill', 'Xvfb'])


if __name__ == '__main__': main()
