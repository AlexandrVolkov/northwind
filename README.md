# Toolchain (не для игроков)

Сборки из исходников для облачных сессий Claude Code: готовые релизы Godot/Ghidra там недоступны, а сборка занимает около часа.

- `godot-4.7.2-mono-linux-x86_64`: Godot 4.7.2-stable .NET editor (`platform=linuxbsd target=editor module_mono_enabled=yes wayland=no alsa=no pulseaudio=no dbus=no speechd=no udev=no fontconfig=no`) и GodotSharp. Нужны .NET SDK 10 и `DOTNET_ROOT`.
- `ghidra-12.2-dev-linux`: Ghidra из тега `Ghidra_12.2_build`, JDK 25. Z3 взят из Maven `tools.aqua:z3-turnkey:4.13.0`, исходник PostgreSQL для BSim-сервера заменён заглушкой; удалены docs, Extensions и FID-базы MSVC.

Установка: `bash install.sh` (проверяет SHA256SUMS). Ветка не связана с историей `main`.
