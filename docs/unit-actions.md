# Действия юнитов (LE:1 `8227..902F`)

`OriginalUnitActions` (`scripts/OriginalUnitActions.cs`) — функции действий юнита, по одной на функцию оригинала; сверка `tools/verify_unit_actions.py` → `tests/fixtures/unit-actions.json` (1000 случаев на набор функций), повтор — `tests/OriginalUnitActionChecks.cs`. Функции вне проверяемой записываются заглушками (регистры в журнал, результаты из случая; поиск шаблона `68BD` меняет только EDX); флаги ZF, CF, SF сверяются.

Перенесены: диспетчер `FS:E1` (`8227`: таблица из шести обработчиков после вызова, состояние `+14 & 7`, следующее состояние `+14 >> 3`, флаги сложения адреса обработчика), действия `8273` (ходьба к точке), `82C3` (бездействие: поиск врага `8D3E`, следование), `833F`, `839F` (захват цели и дальность `8EC7`), `83D9`, `843E` (бой: дальность, замах, захват, подход), помощники `8E3C` (цель — выбранный игроком объект), `8E56` (захват, `+15` бит 10h), `8E81`, `8EC7`, `8F70` (подход и передача цели). 

**Долг:** вызываемые ими функции `8A7F`, `8ABF`, `8AF4`, `8B0F`, `8B37`, `8B4C`, `8BEA`, `8C47`, `8C89`, `8C98`, `8CF3`, `902F` и обработчики `84AA..877F` ещё не перенесены (часть уже есть в `OriginalUnitOrders`, `OriginalCombatRules`); схватка пока не ведёт юнитов этими функциями.

Второй пакет (`877F..8B5E`): `877F`, `878D` (стрелок: кадры стоя, запрос `8D0B`), `881B` (праздный: запрос стоя и случайное место рядом через `10AD2`), `8917` (логово: потомство через `A758`, передача ожидающего рабочего через `C4CD`/`CAEE`/`D1BA`), `89F1` (запрос армии раз в 32 обновления, `+0F` биты 10h/20h), `8A54` (уход юнита: ячейка `[1CCB + 16h·владелец] = FF`, `12588`, `4473`), `8A7F` (цель в зоне: `D182` с AH = 5), `8A9F` и `8ABF` (забыть мёртвую цель; второй добавляет опыт `+16` на `DAT[76] / 4`), `8AF4` (счётчик задержки замаха `+2F` до `DAT[75] − опыт`), `8B0F`, `8B37`, `8B4C`. Флаг SF после `MUL` не сравнивается (по руководству он не определён); CF после `MUL` воспроизведён. Проверка: 2500 случаев на набор из 25 функций.

Третий пакет: `84AA..8659` (действие пастуха/сопровождающего: ячейка `+32/+33`, расстояние `C331`, подсостояние `+34`, блуждание `85E2`), `8BC7` (цель вне досягаемости), `8BEA` и `8C47` (перемещение к цели или к ячейке через `960B`), `8C89` (запрос ходьбы), `8CF3`. Всего 31 функция в `tests/fixtures/unit-actions.json` (2500 случаев).

Четвёртый пакет: `7FA7..8120` (`F7FA7`: чтение следующего шага маршрута — полубайты курсора `+28`, бит 8 = конец, при конце `8120`, сверка с целью `[525]/[526]`, повторный поиск `73F5` в пределах `[1C61]`; шаг идёт через допуск `7C63`: DL = 0 свободно (`43B7`, курсор + 1, AH = 20h), 2 ждать, 3 столкновение (`BB26`: тому, кого задели, `+19` = 0 и `+17` = ..11h), FFh повторный поиск; проваливается в хранилище маршрута `802C`), `902F` (предмет рядом) и `81D9` (туман и занятость, дескриптор клетки юнита). Заглушка `802C` — это отдельно перенесённое хранилище маршрутов (`OriginalRoutePool`). Всего 34 функции в `tests/fixtures/unit-actions.json`.

Пятый пакет: `A10B` (шлюз скорости игры: счётчик `[19E4]` против допуска `[908E]` — клавиши 4Eh/4Ah его меняют —, решение `[1C4E]` «мир шагает», биты `[19E7]` раз в 5 и раз в 2 счёта, зеркало `fs:[13D]`) и `9A8E` (конец шага ходьбы: остаток шага по вектору курса `19C7`, перенос занятости `4442`, `43B7`, `43B0`, освобождение маршрута `8120`). Окна глобальных данных проверки (`19C0`, `1C30`, `4160`, `9080`) заполняются псевдослучайными байтами. Всего 36 функций.

Шестой пакет: `DA45` (счёт объектов списка `[1F45]` по маске владельцев `+12` участника AL: EAX — вне маски, EBX — в маске), `D740` (удар силой BL и видом BH по объекту, найденному `982B`: здоровье падает на силу минус `DAT[44]`, `+17` получает флаги удара), `D77A` (случайная точка вокруг объекта до AL·16, `9E41`/`9E55`, эффект `996F`), `8659`. Всего 40 функций.

Седьмой пакет: `BB8B` (+`BBE4`) — `FS:55`, перерисовка кадра отрезка линии построек и его соседей: таблица за вызовом (EDX байт после адреса возврата; пары «смещение кадра, ключ», FFh — конец и поиск с начала с AL = исходный AL) по ключу `AL маски`, кадр `+1` сдвигается на разность смещений на `DAT[4A]·2 + DAT[4B]`. Список соседей даёт `BADF` (перенесён как `OriginalLineNeighbors`). Заглушка проверки: счётчик инструкций теперь 200000 (поиск ключа в таблице из 256 записей). Всего 41 функция.

Восьмой пакет: шаблонные поиски `6877`, `6885`, `68A0`, `68BD` теперь не заглушки, а перенесённый код (EBX — запись армии, EDI/EDX — шаблон `[запись армии + 4] + 0ABh·номер`), `C4CD` с `C591` (ожидающий рабочий у здания: клетка `DAT[53]/[54]` или спираль вокруг `DAT[03]×DAT[04]` по плоскостям занятости `[18CE]`), `CAEE`, `D12C`/`D157` (класс местности клетки), `D182`/`D19C` (вызов скрипта шаблона с сохранением регистров). Всего 51 функция; объекты проверки ограничены армией 0..2 и шаблоном 0.

Девятый пакет: `C331`/`C344` (большее из расстояний по осям между двумя точками или объектами), `C35F` (первая клетка класса местности AL в окне вокруг юнита: окно `DL·2 + [273F] × [2740]`, обрезка по карте 32 × 32, возврат клетки в AL/BL, ZF = 0 — найдена). Всего 54 функции.

## C439: the nearest object of the chain

`FC439` (LE:1 C439..C4CD) walks the chain of the owner of the object (participant +0xE .. +9), takes the template of every object through the army table (`0x56·army`, `0xAB·index`), keeps those whose `flags & EBX == [1F35]` and whose health is above DAT[46], and returns the one with the least Chebyshev distance in EDI and [1F31]. Verified against x86 on random chains of 0..4 objects (57 cases); EAX/ECX/EDX/EBP, the flags and the globals match.

## C0F6: the action of the building worker

`FC0F6` (LE:1 C0F6..C331) is the action of the unit that builds: state 0 orders the walk (8CA1), 1 waits until the unit stands on the cell, 2 asks the order service (9653), pays the template costs from the participant (+18/+1C/+20/+24) and creates the building through the allocator A758, 3 builds it (health grows by DAT[79] every DAT[4B] steps up to the template maximum, then BL = C0h); every failure ends with BL = 40h. The flags of D363 survive 43B0 through PUSHFD/POPFD. Verified against x86 on 262 cases (19 with the full payment, 96 in the building state); the participant record of the owner is part of the compared output.

## B9EB, BA2E, BC15

`FB9EB` counts the objects of the chain of an owner whose +1B equals BL (and folds their +0E/+0F into CL), `FBA2E` only asks whether one exists (AL = FFh), `FBC15` is the tail of the reshaping BB8B (43B0 for the record, BB8E, 98DB). Verified against x86 on the random chains of the unit-action fixture.

## B5AB, B5D8, B616

`FB5AB` turns the facing of the unit by one step according to the random number of 9E41, `FB5D8` finds the smallest number from 1 not used in +1B by the chain of an owner, `FB616` (with B6B2) walks a rectangle of the three cell planes and writes BL into +1B of the objects of the owner whose template has none of the flags 0Ch and whose +0F is not 0. Verified against x86; sabotaging the template test or the number comparison is caught by the fixture.

## BE2E, BE6F

`FBE2E` asks whether the participant can pay the four costs of a template (AL = 0 when yes); `FBE6F` creates the effect 9727 with the chance of 9E41 when the health is more than a quarter below the template health. Verified against x86 (the ADD flags of ECX/EDX reach the caller).

## CB21, CB77, CC7B, CCC4

`FCB77` is the conversion of a target (the priest-like action): +22 counts the ticks, at 32h the target is handed over (D1BA) and the statistics byte [6BA] + 1A35h * owner + 17Bh + 3 * converter is counted, at 3Ch CB21 turns the target; BL ends 0 as the original (the `MOV BL,2` before `JMP CBB8` is dead). `FCB21` turns the target, `FCC7B` takes its item (1243A), `FCCC4` sets health 1. The fixture gains the statistics records ([6BA]) in the compared output; sabotaging the counter offset is caught.

## CCCB

`FCCCB` is the upkeep of a unit: A716 gives the count, the participant pays 10 * +23 * +22 from +20 (floored at 0, then +23 = 1), the unit pays it from +24; when that runs out A6AE is called and the statistics value +73 of the owner falls by 271h. The original also tests and clears `+73` of the unit record itself (kept; the record of the check is 80h long). Verified against x86; sabotaging 271h is caught.

## B319

`FB319` walks the chain of the owner of the unit and hands every living object whose template has flag 8 and a defined +4E and that is more than 25 cells away (Chebyshev distance >> 5) over to the owner [6C6] - 1 (D1BA). Verified against x86; the fixture drives it with far objects and a suitable template (a changed threshold is caught).

## B6F1

`FB6F1` runs BA2E and 6EC1 for the object ESI ([1969] holds it meanwhile) and then walks the chain (ESI .. EBP): for the objects whose +1B equals [1C87] the 6 slots of 12 bytes at DS:1978 are cleared (+2) unless bit k of +0F is set and the byte +7 of the slot equals the byte of the template entry and is not zero. The slot table joins the compared DS windows. Verified against x86; clearing the wrong field is caught.
