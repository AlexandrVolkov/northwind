# Граф вызовов ИИ поселения (LE:1)

Сгенерировано `tools/call_graph.py` (--start 1E53A --end 23DC2 --root 1E7EA --root 1E53A). Не правьте вручную: статусы берутся из `data/reverse_engineering/annotations.json`, процедуры — рекурсивным спуском по коду.

| Статус | Процедур | Инструкций |
|---|---|---|
| not ported | 12 | 162 |
| partial | 1 | 154 |
| ported | 114 | 5471 |

| Процедура | Инстр. | Статус | Имя | Вызывает (✓ перенесено, ~ частично, · нет) |
|---|---|---|---|---|
| 1E53A | 99 | ported | AI block initialisation from character table | 1EB33✓, 1ECA0✓, 219C3✓ |
| 1E7EA | 34 | not ported |  | 1E87A~, 1ED26✓, 1F078✓, 209B0✓, 20DD0✓, 21E99✓, 221AE✓, 226F1✓, 227C7✓, 2323A✓, 23667✓, 23C11✓ |
| 1E87A | 154 | partial | AI settlement census | 68BD✓, 1EC3F·, 219C3✓ |
| 1EAD8 | 6 | ported | AI block of participant |  |
| 1EAED | 24 | ported | Add AI plan record |  |
| 1EB33 | 6 | ported | Clear AI plan list |  |
| 1EB40 | 13 | ported | Find or add AI plan |  |
| 1EB5C | 6 | ported | Find or add AI plan | 1EB40✓ |
| 1EBEF | 24 | ported | Random explored cell for AI patrol | 9E41✓, 9E55✓, 219C3✓ |
| 1EC3F | 20 | not ported |  |  |
| 1EC6E | 18 | ported | Area in sight of an object | 6885✓, 218A8✓, 219C3✓ |
| 1ECA0 | 30 | ported | First living object of a DAT class | 68BD✓ |
| 1ECF8 | 7 | not ported |  |  |
| 1ED0F | 7 | not ported |  |  |
| 1ED26 | 29 | ported | AI squad step loop and steps 0..3 | 1ED77✓, 1ED78✓, 1ED87✓, 1EE02✓, 1EF26·, 1EFCC✓, 205FB✓ |
| 1ED77 | 1 | ported | AI squad step loop and steps 0..3 |  |
| 1ED78 | 6 | ported | AI squad step loop and steps 0..3 | B8A2✓ |
| 1ED87 | 44 | ported | AI squad step loop and steps 0..3 | B765✓, B977✓ |
| 1EDFD | 2 | ported | AI squad step loop and steps 0..3 |  |
| 1EE02 | 82 | ported | AI squad step loop and steps 0..3 | 6885✓, 68BD✓, 20408✓, 20458✓, 207CB✓ |
| 1EF21 | 2 | ported | AI squad step loop and steps 0..3 |  |
| 1EF26 | 40 | not ported |  | 6996✓, A4C2·, 10075·, 10262·, 1037E·, 103AF· |
| 1EFB8 | 7 | not ported |  |  |
| 1EFCC | 16 | ported | AI squad step 5: formation march | B7B9✓ |
| 1EFF9 | 4 | not ported |  |  |
| 1F004 | 5 | not ported |  |  |
| 1F011 | 15 | not ported |  | 219C3✓ |
| 1F03B | 9 | not ported |  |  |
| 1F054 | 9 | not ported |  |  |
| 1F06D | 5 | not ported |  |  |
| 1F078 | 30 | ported | AI squad states 0..3 and the state loop | 1F0D5✓, 1F1DF✓, 1F31E✓, 1F3F5✓, 1F476✓, 1F653✓, 1F72C✓, 1F7CA✓, 1F8CF✓, 1FA01✓, 1FAD9✓ |
| 1F0D5 | 71 | ported | AI squad states 0..3 and the state loop | 1EB5C✓, 1ECF8·, 1EDFD✓, 1EF21✓, 1F004·, 1F03B·, 1F06D·, 1FCED✓, 1FE78✓, 1FECE✓, 1FF74✓, 201B8✓, 20458✓, 215F1✓ |
| 1F1DF | 90 | ported | AI squad states 0..3 and the state loop | 1EB5C✓, 1ED0F·, 1F3E9✓, 1FBB1✓, 1FC28✓, 1FCED✓, 1FE78✓, 1FECE✓, 1FF00✓, 1FFB7✓, 20071✓, 201B8✓, 205E7✓, 219C3✓ |
| 1F306 | 8 | ported | AI squad states 0..3 and the state loop |  |
| 1F31E | 50 | ported | AI squad states 0..3 and the state loop | 9E41✓, 1ECF8·, 1FBB1✓, 1FCED✓, 1FE78✓, 1FECE✓, 1FFB7✓, 20001✓, 20071✓, 201B8✓, 205E7✓ |
| 1F3E9 | 4 | ported | AI squad states 0..3 and the state loop |  |
| 1F3F5 | 32 | ported | AI squad states 0..3 and the state loop | 1ECF8·, 1F3E9✓, 1FC6B✓, 1FCED✓, 1FE78✓, 1FECE✓, 1FFB7✓, 205E7✓ |
| 1F464 | 6 | ported | AI squad states 0..3 and the state loop |  |
| 1F476 | 106 | ported | AI squad state 9: timber workers | 6885✓, C439✓, 1EB5C✓, 1EDFD✓, 1F004·, 1F06D·, 1FE97✓, 201B8✓, 205E7✓, 206D5✓, 20730✓, 219C3✓, 224D1✓, 2258F✓ |
| 1F5F1 | 16 | ported | Set worker squad state 9 with its target area | 1EDFD✓, 1F004·, 219C3✓ |
| 1F653 | 56 | ported | AI squad state 10: mine workers | 6885✓, C439✓, 1EB5C✓, 201B8✓, 205E7✓, 206D5✓, 219C3✓ |
| 1F71A | 5 | ported | Set worker squad state 10 |  |
| 1F72C | 45 | ported | AI squad states 4..8 | 1FBB1✓, 1FCED✓, 1FE78✓, 1FECE✓, 1FFB7✓, 201B8✓, 205E7✓ |
| 1F7CA | 69 | ported | AI squad states 4..8 | 1F3E9✓, 1FC6B✓, 1FCED✓, 1FE78✓, 1FECE✓, 1FF19✓, 201B8✓, 201F6✓, 20232✓, 205E7✓ |
| 1F8C0 | 5 | ported | AI squad states 4..8 |  |
| 1F8CF | 80 | ported | AI squad states 4..8 | 1EB5C✓, 1ECF8·, 1F054·, 1F3E9✓, 1FBB1✓, 1FC28✓, 1FCED✓, 1FE78✓, 1FECE✓, 1FFB7✓, 201B8✓, 20458✓, 2160B✓ |
| 1F9EC | 6 | ported | AI squad states 4..8 |  |
| 1FA01 | 58 | ported | AI squad states 4..8 | 1EB5C✓, 1EBEF✓, 1FBB1✓, 1FC28✓, 1FCED✓, 1FE78✓, 1FE8A✓, 1FECE✓, 1FFB7✓, 201B8✓, 205E7✓, 219C3✓ |
| 1FACD | 4 | ported | AI squad states 4..8 |  |
| 1FAD9 | 56 | ported | AI squad states 4..8 | 1ED0F·, 1F3E9✓, 1FC28✓, 1FCED✓, 1FE78✓, 1FECE✓, 201B8✓, 205E7✓ |
| 1FB9C | 6 | ported | AI squad states 4..8 |  |
| 1FBB1 | 34 | ported | AI squad retreat, march, fight and strength helpers | 6996✓, 1FC6B✓, 1FF51✓, 20071✓ |
| 1FC28 | 26 | ported | AI squad retreat, march, fight and strength helpers | 1FC6B✓ |
| 1FC6B | 26 | ported | AI squad retreat, march, fight and strength helpers | 1EFB8·, 1F06D· |
| 1FCED | 101 | ported | AI squad retreat, march, fight and strength helpers | 1EF21✓, 1EFB8·, 1EFF9·, 1F011·, 1F054·, 1F06D·, 1FF19✓, 1FF2E✓, 1FF51✓ |
| 1FE78 | 8 | ported | AI squad retreat, march, fight and strength helpers |  |
| 1FE8A | 5 | ported | AI squad retreat, march, fight and strength helpers |  |
| 1FE97 | 5 | ported | AI squad retreat, march, fight and strength helpers |  |
| 1FECE | 18 | ported | AI squad retreat, march, fight and strength helpers | 219C3✓ |
| 1FF00 | 9 | ported | AI squad retreat, march, fight and strength helpers |  |
| 1FF19 | 10 | ported | AI squad retreat, march, fight and strength helpers |  |
| 1FF2E | 10 | ported | AI squad retreat, march, fight and strength helpers | 21CA0✓ |
| 1FF51 | 10 | ported | AI squad retreat, march, fight and strength helpers | 21CA0✓ |
| 1FF74 | 24 | ported | AI squad retreat, march, fight and strength helpers | 20275✓, 20458✓ |
| 1FFB7 | 26 | ported | AI squad retreat, march, fight and strength helpers | 1FF51✓, 20275✓ |
| 20001 | 39 | ported | AI squad retreat, march, fight and strength helpers | BA60·, D7B1✓, DA7F·, 1F011· |
| 20071 | 10 | ported | AI squad retreat, march, fight and strength helpers | 1EAD8✓ |
| 20095 | 8 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups |  |
| 200AB | 3 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups |  |
| 200B5 | 24 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups | 20095✓, 200AB✓, 2010B✓, 20189✓, 201B8✓ |
| 2010B | 26 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups | 20232✓ |
| 20143 | 29 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups | 200AB✓ |
| 20189 | 18 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups |  |
| 201B8 | 19 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups | 20143✓ |
| 201F6 | 18 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups | 20143✓ |
| 20232 | 13 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups |  |
| 20275 | 18 | ported | AI squad registry: numbers, create, remove, disband, transfer, lookups |  |
| 2029F | 22 | ported | Free units join a squad up to a strength | 2035A✓ |
| 202FB | 30 | ported | Squad members leave up to a strength |  |
| 2035A | 49 | ported | Nearest free object of a class |  |
| 20408 | 73 | ported | Nearest visible hostile object in a cell area | 6877✓, 2055C✓ |
| 20458 | 74 | ported | Nearest visible hostile object in a cell area | 2055C✓ |
| 2055C | 41 | ported | Nearest visible hostile object in a cell area |  |
| 205E7 | 6 | ported | Nearest visible hostile object in a cell area | 1F011·, 20458✓ |
| 205FB | 70 | ported | AI squad census from its members |  |
| 206D5 | 31 | ported | Random idle member of a squad | 9E41✓ |
| 20730 | 22 | ported | Squad member with an activity code |  |
| 207CB | 51 | ported | Squad stands; members outside the area return |  |
| 2095D | 20 | ported | Squad of a state whose area holds a cell |  |
| 2098B | 15 | ported | Find AI squad by state and target object |  |
| 209B0 | 277 | ported | AI garrison: unit orders, building guards, hostile buildings, patrols, wandering | 68BD✓, 9E41✓, 9E55✓, 9E69✓, D7B1✓, 1EB5C✓, 1FACD✓, 202FB✓, 20408✓, 2098B✓, 2160B✓, 21696✓, 216DF✓, 219C3✓, 21CA0✓ |
| 20DD0 | 420 | ported | AI military requests (list +1AB, modes 1..10) | 9E41✓, 1EB40✓, 1EB5C✓, 1EC6E✓, 1F306✓, 1F464✓, 1F8C0✓, 1F9EC✓, 1FB9C✓, 20232✓, 2029F✓, 214C3✓, 21555✓, 215F1✓, 2160B✓, 21660✓, 216DF✓, 22E46✓ |
| 214C3 | 39 | ported | AI raid targets: random building, nearest mine | 68BD✓, 9E41✓, C331· |
| 21555 | 42 | ported | AI raid targets: random building, nearest mine | 68BD✓, C331· |
| 215F1 | 10 | ported | AI strength wanted for an area (215F1) and against an object (2160B) |  |
| 2160B | 22 | ported | AI strength wanted for an area (215F1) and against an object (2160B) | 68BD✓, C331· |
| 21660 | 12 | ported | AI squad recruitment (21660/21696 free units, 216DF nearest with lower-priority squads) | 21696✓ |
| 21696 | 30 | ported | AI squad recruitment (21660/21696 free units, 216DF nearest with lower-priority squads) | 200B5✓, 201B8✓, 2029F✓ |
| 216DF | 55 | ported | AI squad recruitment (21660/21696 free units, 216DF nearest with lower-priority squads) | 68BD✓, 200B5✓, 201B8✓, 21DD4✓ |
| 218A8 | 52 | ported | Friendly and hostile strength in an area | 21966✓ |
| 21966 | 26 | ported | Friendly and hostile strength in an area |  |
| 219C3 | 23 | ported | Clip a centred area to the map |  |
| 21CA0 | 59 | ported | Hostile objects of an area for AI squads | D7B1✓ |
| 21DD4 | 53 | ported | Nearest object for a squad of a priority | 6885✓, 20232✓ |
| 21E99 | 210 | ported | AI economy planning: income, upkeep, economy requests, farm and building orders | EDD5✓, 1EB5C✓, 2098B✓, 22E46✓ |
| 221AE | 131 | ported | AI economy executor over list +4F3 (workers, mines, lumbermill) | D363·, 1EB5C✓, 1F5F1✓, 1F71A✓, 22435✓, 22613✓, 22E46✓ |
| 223B4 | 34 | ported | List templates of a DAT class | A673✓ |
| 22435 | 47 | ported | Gather free workers into a squad | 200B5✓, 201B8✓ |
| 224D1 | 56 | ported | Nearest terrain cell of a class in the squad area |  |
| 2258F | 49 | ported | Random seen grove cell around the squad centre | 9E41✓, 9E55✓, 2095D✓, 219C3✓, 2268E✓ |
| 22613 | 38 | ported | Random seen ore cell around the home for a mine | 9E41✓, 9E55✓, 219C3✓ |
| 2268E | 33 | ported | Terrain class present in an area |  |
| 226F1 | 57 | ported | AI upkeep: repair requests and decay | 68BD✓ |
| 227C7 | 469 | ported | AI order executor (list +34F, modes 1..10) | 68A0✓, 9E41✓, 9E55✓, 1EAED✓, 1EB5C✓, 2035A✓, 219C3✓, 22E46✓, 22E85✓, 22F76✓, 22F82✓, 22FE4✓, 23201✓ |
| 22E46 | 26 | ported | Random open template of a DAT class | 68A0✓, 9E41✓, 223B4✓ |
| 22E85 | 88 | ported | Producer of a template (WRK AH=6 tables) | 6885✓, 9E41✓, A673✓, D19C· |
| 22F76 | 4 | ported | Produce (60h) and build (58h) commands |  |
| 22F82 | 32 | ported | Produce (60h) and build (58h) commands |  |
| 22FE4 | 86 | ported | Building site check around a cell | 68A0✓, 68BD✓, AF07· |
| 23201 | 24 | ported | Object of a class in an area | 6885✓, D7B1✓ |
| 2323A | 267 | ported | AI turn: tribute, collapse, relation decisions | 43E5✓, 9E41✓, 9E55✓, A758~, EDD5✓, EE69✓, 10AD2·, 1EAD8✓, 1EB5C✓, 1ECA0✓, 22E46✓, 22FE4✓, 23A95✓, 23B10✓, 23B6A✓ |
| 23667 | 243 | ported | AI plan executor | EDD5✓, EE69✓, 1EB40✓, 1EB5C✓, 23ABB✓, 23B10✓ |
| 23A95 | 14 | ported | AI has plan for target |  |
| 23ABB | 31 | ported | Cancel military requests against target |  |
| 23B10 | 29 | ported | AI territory threat by owner | 68BD✓, D7B1✓ |
| 23B6A | 15 | ported | Territories overlap |  |
| 23C11 | 116 | ported | AI development step: laboratory and request list | 9E41✓, 1EB5C✓, 1ECA0✓, 223B4✓, 22E46✓ |

## Вызываемое вне диапазона

| Процедура | Статус | Имя | Кто вызывает |
|---|---|---|---|
| 43E5 | ported | Occupancy footprint writer wrapper | 2323A |
| 6877 | ported | Participant record and DAT template of an object | 20408 |
| 6885 | ported | Participant record and DAT template of an object | 1EC6E, 1EE02, 1F476, 1F653, 21DD4, 22E85, 23201 |
| 68A0 | ported | Participant record and DAT template of an object | 227C7, 22E46, 22FE4 |
| 68BD | ported | Participant record and DAT template of an object | 1E87A, 1ECA0, 1EE02, 209B0, 214C3, 21555, 2160B, 216DF, 226F1, 22FE4, 23B10 |
| 6996 | ported | Direction byte between two points (arctangent table DS:287E) | 1EF26, 1FBB1 |
| 9E41 | ported | Random byte streams [19EB]/[19EC] over table DS:297E | 1EBEF, 1F31E, 206D5, 209B0, 20DD0, 214C3, 2258F, 22613, 227C7, 22E46, 22E85, 2323A, 23C11 |
| 9E55 | ported | Random byte streams [19EB]/[19EC] over table DS:297E | 1EBEF, 209B0, 2258F, 22613, 227C7, 2323A |
| 9E69 | ported | Random byte stream [19ED] over table DS:297E | 209B0 |
| A4C2 | unknown |  | 1EF26 |
| A673 | ported | Template open in development tree | 223B4, 22E85 |
| A758 | partial | Allocate and initialize object record | 2323A |
| AF07 | unknown |  | 22FE4 |
| B765 | ported | Group orders to squad members and squad bounds | 1ED87 |
| B7B9 | ported | Group orders to squad members and squad bounds | 1EFCC |
| B8A2 | ported | Group orders to squad members and squad bounds | 1ED78 |
| B977 | ported | Group orders to squad members and squad bounds | 1ED87 |
| BA60 | unknown |  | 20001 |
| C331 | unknown |  | 214C3, 21555, 2160B |
| C439 | ported | Find worker cargo receiver | 1F476, 1F653 |
| D19C | unknown |  | 22E85 |
| D363 | unknown |  | 221AE |
| D7B1 | ported | Collect unique occupants of an area | 20001, 209B0, 21CA0, 23201, 23B10 |
| DA7F | unknown |  | 20001 |
| EDD5 | ported | Queue and dispatch a message | 21E99, 2323A, 23667 |
| EE69 | ported | Find a queued message by type and party | 2323A, 23667 |
| 10075 | unknown |  | 1EF26 |
| 10262 | unknown |  | 1EF26 |
| 1037E | unknown |  | 1EF26 |
| 103AF | unknown |  | 1EF26 |
| 10AD2 | unknown |  | 2323A |
