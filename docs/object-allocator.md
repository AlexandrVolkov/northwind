# Распределитель объектов (FS:45 = LE:1 `A758..A870`)

`OriginalObjectAllocator` (`scripts/OriginalObjectAllocator.cs`), сверка с x86 — `tools/verify_object_alloc.py` → `tests/fixtures/object-alloc.json` (800 случаев, в том числе переполнение цепочки), повтор — `tests/OriginalObjectAllocChecks.cs`.

Объекты участника лежат в одной цепочке пула: `участник +5` — первый объект, `+9` — смещение конца цепочки, `+0Eh` — смещение подсказки поиска. У объекта `+10h` — его размер в цепочке (размер шаблона `DAT[0]` плюс запас). Поиск «первый подходящий»: сначала зазор между первым объектом и подсказкой, затем запас каждого объекта за размером его шаблона (запас целиком отдаётся новому объекту, у старого размер уменьшается), иначе цепочка растёт за последним объектом (отказ, если от первого объекта уже 10000h байт — возврат AL = 1, ZF = 0). Запись очищается (размер шаблона байт), размер в цепочке сохраняется; затем армия `+0`, владелец `+3`, шаблон `+0E`, позиция `+4/+8`, слой `+0D`, состояние `+14 = 80h`, здоровье `+19/+1A` (`DAT[45]`), кадр `+1/+2` (`DAT[48]`), `+20 = FFh`, и скрипт шаблона (`WRK + DAT[3B]`) вызывается с AH = 0 и тремя аргументами из стека (EBX, ECX, EDX).

Глобальные переменные распределителя: `[19C3]` — адрес скрипта, `[1F44]` — здоровье, `[1F2F]` — кадр. Вызов скрипта в проверке — записанная заглушка. Схватка пока создаёт объекты своим кодом (адаптер), подключение распределителя — отдельный срез.

## Hand-over of an object (D1BA)

`OriginalObjectAllocator.Transfer` (LE:1 D1BA..D2D1) copies the record of an object into the first-fit block of the chain of the new owner (`FindBlock`, the search shared with A758), keeps the length of the block, sets the owner (+3), marks the old record dead (+17 |= 280h, &= FEFFh, health 0) and clears 80h/200h of +17 of the copy. The notification 9A8E is made first when the template flags have none of 148008h. The copy is the forward REP MOVSB. Verified against x86 on 800 cases (`tools/verify_object_transfer.py`, `tests/fixtures/object-transfer.json`: 51 failures of a full chain, 272 notifications, 618 carved blocks); the 4-byte global at 273Fh is restored.
