# Туман войны: открытие клеток

Сгенерировано `tools/call_graph.py` (--start 8100 --end 81d9 --root 813e --root 8198). Не правьте вручную: статусы берутся из `data/reverse_engineering/annotations.json`, процедуры — рекурсивным спуском по коду.

| Статус | Процедур | Инструкций |
|---|---|---|
| ported | 2 | 59 |

| Процедура | Инстр. | Статус | Имя | Вызывает (✓ перенесено, ~ частично, · нет) |
|---|---|---|---|---|
| 813E | 36 | ported | Reveal a clipped square by persistent owner-bit clearing | 6885✓ |
| 8198 | 23 | ported | Reveal a clipped square by persistent owner-bit clearing |  |

## Вызываемое вне диапазона

| Процедура | Статус | Имя | Кто вызывает |
|---|---|---|---|
| 6885 | ported | Participant record and DAT template of an object | 813E |
