#!/bin/bash
# Restore prebuilt Godot 4.7.2 .NET and Ghidra 12.2 into /opt for cloud sessions.
# Usage: git fetch --depth 1 origin toolchain && git worktree add /tmp/tc origin/toolchain && bash /tmp/tc/install.sh
set -e
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
for name in godot-4.7.2-mono-linux-x86_64 ghidra-12.2-dev-linux; do
  cat "$here/$name.tar.xz.part"* > "$work/$name.tar.xz"
done
(cd "$work" && sha256sum -c "$here/SHA256SUMS")
mkdir -p /opt/godot /opt/ghidra
tar xJf "$work/godot-4.7.2-mono-linux-x86_64.tar.xz" -C /opt/godot
tar xJf "$work/ghidra-12.2-dev-linux.tar.xz" -C /opt/ghidra
ln -sfn /opt/ghidra/ghidra_12.2_DEV /opt/ghidra/current
ln -sf /opt/godot/godot-4.7.2-mono/godot.linuxbsd.editor.x86_64.mono /usr/local/bin/godot
rm -rf "$work"
echo "godot: $(godot --version 2>/dev/null)"; echo "ghidra: /opt/ghidra/current"
