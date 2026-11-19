# Экран авторов: вкладки и видео

Сгенерировано `tools/call_graph.py` (--start 1c4b5 --end 1cbf7 --root 1c4b5 --root 1c5d0 --root 1c6f7 --root 1c802 --root 1c917 --root 1c93e --root 1c9ed --root 1ca3a --root 1ca6b). Не правьте вручную: статусы берутся из `data/reverse_engineering/annotations.json`, процедуры — рекурсивным спуском по коду.

| Статус | Процедур | Инструкций |
|---|---|---|
| not ported | 12 | 467 |

| Процедура | Инстр. | Статус | Имя | Вызывает (✓ перенесено, ~ частично, · нет) |
|---|---|---|---|---|
| 1C4B5 | 40 | not ported |  | 3D40·, 3D67·, 3DB5✓, 3DF2·, 114E8·, 18CE5·, 18E65·, 18EC5·, 194B2·, 1C56F·, 1C5AC·, 1CA6B·, 1CC83·, 1CCA6· |
| 1C56F | 14 | not ported |  | 1C5AC·, 1CB69· |
| 1C5AC | 5 | not ported |  |  |
| 1C5D0 | 95 | not ported |  | 18699·, 186C6·, 186F7·, 18ED4·, 18EF5· |
| 1C6F7 | 73 | not ported |  | 18699·, 186C6·, 186F7·, 1CC32· |
| 1C802 | 77 | not ported |  | 18699·, 186C6·, 186F7·, 1CC32· |
| 1C917 | 10 | not ported |  | 1CB69·, 1CC32· |
| 1C93E | 33 | not ported |  | 15B97·, 183BA·, 1CB69·, 1CC32· |
| 1C9ED | 17 | not ported |  | 1CB69·, 1CC32·, 1CC4E·, 1CC71· |
| 1CA3A | 10 | not ported |  | 1CB69·, 1CC32· |
| 1CA6B | 55 | not ported |  | 3EEA·, 1588B·, 15A2E·, 1847A·, 186C6·, 186F7· |
| 1CB69 | 38 | not ported |  | 16004·, 186F7· |

## Вызываемое вне диапазона

| Процедура | Статус | Имя | Кто вызывает |
|---|---|---|---|
| 3D40 | unknown |  | 1C4B5 |
| 3D67 | unknown |  | 1C4B5 |
| 3DB5 | ported | Advance cursor frame on a four-tick byte timer interval | 1C4B5 |
| 3DF2 | unknown |  | 1C4B5 |
| 3EEA | unknown |  | 1CA6B |
| 114E8 | unknown |  | 1C4B5 |
| 1588B | unknown |  | 1CA6B |
| 15A2E | unknown |  | 1CA6B |
| 15B97 | unknown |  | 1C93E |
| 16004 | unknown |  | 1CB69 |
| 183BA | unknown |  | 1C93E |
| 1847A | unknown |  | 1CA6B |
| 18699 | unknown |  | 1C5D0, 1C6F7, 1C802 |
| 186C6 | unknown |  | 1C5D0, 1C6F7, 1C802, 1CA6B |
| 186F7 | unknown |  | 1C5D0, 1C6F7, 1C802, 1CA6B, 1CB69 |
| 18CE5 | unknown |  | 1C4B5 |
| 18E65 | unknown |  | 1C4B5 |
| 18EC5 | unknown |  | 1C4B5 |
| 18ED4 | unknown |  | 1C5D0 |
| 18EF5 | unknown |  | 1C5D0 |
| 194B2 | unknown |  | 1C4B5 |
| 1CC32 | unknown |  | 1C6F7, 1C802, 1C917, 1C93E, 1C9ED, 1CA3A |
| 1CC4E | unknown |  | 1C9ED |
| 1CC71 | unknown |  | 1C9ED |
| 1CC83 | unknown |  | 1C4B5 |
| 1CCA6 | unknown |  | 1C4B5 |
