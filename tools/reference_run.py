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
DS_LINEAR = 0x17F000   # the linear address of the data segment (the same in every run; the emulated RAM does not start at the beginning of the host region)


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
    """(host address of the emulated linear address 0, host address of the data segment) of the running game. The signature may occur more than once (file buffers): the right copy has a plausible pool pointer [6AA] (or, before a mission, [1C49])."""
    best = None
    with open(f'/proc/{p}/mem', 'rb', 0) as mem:
        for a, b in regions(p):
            try: mem.seek(a); data = mem.read(b - a)
            except OSError: continue
            i = data.find(SIGNATURE)
            while i >= 0:
                ds = i - SIGNATURE_OFFSET
                pool = struct.unpack_from('<I', data, ds + 0x6aa)[0]; vtab = struct.unpack_from('<I', data, ds + 0x1c49)[0]
                if pool and pool < b - a or vtab and vtab < b - a: return a + ds - DS_LINEAR, a + ds
                if best is None: best = (a + ds - DS_LINEAR, a + ds)
                i = data.find(SIGNATURE, i + 1)
    if best: return best
    sys.exit('the data segment signature was not found (is the game past its start-up?)')


def peek(offset, length=16):
    p = pid(); region, ds = find_ds(p)
    with open(f'/proc/{p}/mem', 'rb', 0) as mem: mem.seek(ds + offset); return mem.read(length)


def objects():
    """The objects of every participant (chain from [97A + F5 * owner + 5] to +9): owner army template x y state(+2D) health(+19) route(+28)."""
    p = pid(); region, ds = find_ds(p)
    with open(f'/proc/{p}/mem', 'rb', 0) as mem:
        def rd(a, n): mem.seek(a); return mem.read(n)
        pool = struct.unpack('<I', rd(ds + 0x6aa, 4))[0]
        out = []
        for owner in range(8):
            part = ds + 0x97a + 0xf5 * owner
            first, end = struct.unpack('<I', rd(part + 5, 4))[0], pool + struct.unpack('<I', rd(part + 9, 4))[0]   # +5 absolute, +9 relative to the pool
            if first == 0 or end <= first or end - first > 0x40000: continue
            cur = first
            while cur < end:
                r = rd(region + cur, 0x36)
                size = struct.unpack_from('<I', r, 0x10)[0]
                out.append((owner, r[0], r[0xe], struct.unpack_from('<I', r, 4)[0], struct.unpack_from('<I', r, 8)[0], r[0x2d], r[0x19], struct.unpack_from('<I', r, 0x28)[0], cur - pool))
                if size == 0: break
                cur += size
        return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('command', choices=['start', 'shot', 'click', 'key', 'peek', 'objects', 'mission', 'snapshot', 'stop'])
    ap.add_argument('args', nargs='*')
    ap.add_argument('--conf', default='/opt/game/run1/dosbox.conf'); ap.add_argument('--display', default=':77'); ap.add_argument('--out', default='shot.png')
    a = ap.parse_args()
    env = dict(os.environ, DISPLAY=a.display)
    if a.command == 'start':
        subprocess.Popen(['Xvfb', a.display, '-screen', '0', '800x600x24'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL); time.sleep(2)
        subprocess.Popen(['dosbox-x', '-conf', a.conf, '-fastlaunch'], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL); time.sleep(20)
    elif a.command == 'shot': run(['import', '-window', 'root', a.out], env=env)
    elif a.command == 'click': run(['xdotool', 'mousemove', a.args[0], a.args[1]], env=env); time.sleep(0.7); run(['xdotool', 'mousedown', '1'], env=env); time.sleep(0.3); run(['xdotool', 'mouseup', '1'], env=env)   # a plain click is too short for the game
    elif a.command == 'key': run(['xdotool', 'key', *a.args], env=env)
    elif a.command == 'peek': print(peek(int(a.args[0], 0), int(a.args[1], 0) if len(a.args) > 1 else 16).hex())
    elif a.command == 'mission':   # from the start of the game to the first mission with the hero Oleg (names: Igor, Oleg, Eirik)
        def clk(x, y): run(['xdotool', 'mousemove', str(x), str(y)], env=env); time.sleep(0.7); run(['xdotool', 'mousedown', '1'], env=env); time.sleep(0.3); run(['xdotool', 'mouseup', '1'], env=env)
        def key(*k):
            for x in k: run(['xdotool', 'key', x], env=env); time.sleep(0.4)
        key('Return'); time.sleep(3); clk(418, 283); time.sleep(5); clk(300, 303); time.sleep(3); key('shift+j', 'k', 't', 'u'); time.sleep(0.5); key('Return'); time.sleep(12); clk(122, 129)
        for _ in range(60):
            time.sleep(5)
            try:
                if struct.unpack('<I', peek(0x6aa, 4))[0] != 0: break
            except SystemExit: pass
        time.sleep(5)
    elif a.command == 'snapshot':   # the first 72 MB of the emulated linear memory (code, data, pool, map, planes) into a file for the Unicorn oracles; not for Git
        p = pid(); region, ds = find_ds(p)
        with open(f'/proc/{p}/mem', 'rb', 0) as mem, open(a.out, 'wb') as f:
            mem.seek(region)
            for _ in range(72): f.write(mem.read(1 << 20))
        print('snapshot written', a.out, 'DS linear', hex(DS_LINEAR))
    elif a.command == 'objects':
        for o in objects(): print(*o)
    else: run(['pkill', 'dosbox-x']); run(['pkill', 'Xvfb'])


if __name__ == '__main__': main()
