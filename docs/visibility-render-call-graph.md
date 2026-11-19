# Туман войны: карта и миникарта

Сгенерировано `tools/call_graph.py` (--start 9f38 --end a320 --root 9f38 --root a1d1). Не правьте вручную: статусы берутся из `data/reverse_engineering/annotations.json`, процедуры — рекурсивным спуском по коду.

| Статус | Процедур | Инструкций |
|---|---|---|
| partial | 2 | 234 |

| Процедура | Инстр. | Статус | Имя | Вызывает (✓ перенесено, ~ частично, · нет) |
|---|---|---|---|---|
| 9F38 | 145 | partial | Fog viewport scratch and weighted corner shading | 1E133· |
| A1D1 | 89 | partial | Explored minimap pixels and camera outline | 3EEA· |

## Вызываемое вне диапазона

| Процедура | Статус | Имя | Кто вызывает |
|---|---|---|---|
| 3EEA | unknown |  | A1D1 |
| 1E133 | unknown |  | 9F38 |
