#!/bin/bash
# Drive the original in DOSBox under Xvfb for reference screenshots (docs/interface.md).
#   tools/dosbox_control.sh start RUN_DIR      start Xvfb :77 and DOSBox with RUN_DIR/dosbox.conf (see docs/original-launch.md)
#   tools/dosbox_control.sh shot FILE.png      screenshot of the 640x480 DOSBox window
#   tools/dosbox_control.sh click X Y          left click at game coordinates (press is held 0.25 s, shorter clicks are missed)
#   tools/dosbox_control.sh move X Y | keys K1 K2 ...
# Hero names accepted by the game: Олег, Игорь, Эйрик; the DOS keyboard is Russian (Олег = j k t u).
export DISPLAY=${DISPLAY:-:77}
if [ "$1" = start ]; then
  pgrep -f "Xvfb $DISPLAY" >/dev/null || (Xvfb $DISPLAY -screen 0 1024x768x24 >/dev/null 2>&1 &); sleep 1
  (cd "$2" && SDL_AUDIODRIVER=dummy dosbox -conf dosbox.conf -exit >/dev/null 2>&1 &); exit 0
fi
W=$(xdotool search --class dosbox | head -1)
G=$(xdotool getwindowgeometry $W | awk '/Position/{print $2}'); X0=${G%,*}; Y0=${G#*,}
case $1 in
 shot) import -window $W "$2";;
 click) xdotool mousemove $((X0+$2)) $((Y0+$3)); sleep 0.3; xdotool mousedown 1; sleep 0.25; xdotool mouseup 1;;
 move) xdotool mousemove $((X0+$2)) $((Y0+$3));;
 keys) shift; for k in "$@"; do xdotool keydown $k; sleep 0.1; xdotool keyup $k; sleep 0.2; done;;
esac
