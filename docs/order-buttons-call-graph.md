# Кнопки приказов: маршруты режима DAT

Сгенерировано `tools/call_graph.py` (--start 4900 --end 6294 --root 61c3 --root 4d41 --root 4e27 --root 4fce --root 50e5 --root 51ac --root 5297 --root 538d --root 53c4 --root 54b4 --root 5593 --root 622f). Не правьте вручную: статусы берутся из `data/reverse_engineering/annotations.json`, процедуры — рекурсивным спуском по коду.

| Статус | Процедур | Инструкций |
|---|---|---|
| not ported | 12 | 140 |
| ported | 1 | 30 |

| Процедура | Инстр. | Статус | Имя | Вызывает (✓ перенесено, ~ частично, · нет) |
|---|---|---|---|---|
| 4938 | 8 | not ported |  |  |
| 4D41 | 3 | not ported |  |  |
| 4E27 | 4 | not ported |  |  |
| 4FCE | 12 | not ported |  | 68A0✓, AE57· |
| 50E5 | 3 | not ported |  |  |
| 51AC | 21 | not ported |  | 68A0✓, AE57·, D182· |
| 5297 | 3 | not ported |  |  |
| 538D | 7 | not ported |  | DEE4· |
| 53C4 | 3 | not ported |  |  |
| 54B4 | 54 | not ported |  | 6885✓, D182·, DEE4·, DF7E·, E055·, F781·, 14E51· |
| 5593 | 9 | not ported |  | DEE4·, DF2A· |
| 61C3 | 30 | ported | Activate command widget and route DAT mode | 4938·, 6885✓, 68A0✓, AE57·, B8A2✓, D182·, DEE4·, DF2A·, DF7E·, E055·, F781·, 14E51· |
| 622F | 13 | not ported |  | 4938·, B8A2✓ |

## Вызываемое вне диапазона

| Процедура | Статус | Имя | Кто вызывает |
|---|---|---|---|
| 6885 | ported | Participant record and DAT template of an object | 54B4, 61C3 |
| 68A0 | ported | Participant record and DAT template of an object | 4FCE, 51AC, 61C3 |
| AE57 | unknown |  | 4FCE, 51AC, 61C3 |
| B8A2 | ported | Group orders to squad members and squad bounds | 61C3, 622F |
| D182 | unknown |  | 51AC, 54B4, 61C3 |
| DEE4 | unknown |  | 538D, 54B4, 5593, 61C3 |
| DF2A | unknown |  | 5593, 61C3 |
| DF7E | unknown |  | 54B4, 61C3 |
| E055 | unknown |  | 54B4, 61C3 |
| F781 | unknown |  | 54B4, 61C3 |
| 14E51 | unknown |  | 54B4, 61C3 |
