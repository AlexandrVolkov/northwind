#!/usr/bin/env python3
"""Verify the unit action handlers of LE:1 (8227..9210: the E1 dispatcher 8227, the action handlers 8273, 82C3, 833F, 839F, 83D9, 843E, 84AA ..., the helpers 8E3C, 8E56, 8E81, 8EC7, 8F70, ...)
with Unicorn, one function at a time. The callees that are not part of the checked function are recorded stubs: their registers are logged and their results (EAX, EBX, ECX, EDX, EDI, ZF) come from the
case. Template lookups (68BD) give EDX = the template buffer. Output: the record, the object and the globals after the run, the registers and the flags (ZF, CF, SF) and the log of the calls."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants

# name: (entry, end, the callee entries that are stubbed)
FUNCS = {
    'f8e3c': (0x8e3c, 0x8e56, []),
    'f8e56': (0x8e56, 0x8e81, [0x68bd]),
    'f8e81': (0x8e81, 0x8ec7, [0x8d89, 0x8abf, 0x8af4, 0xd895, 0x8bea, 0x8b5e, 0x8c7c]),
    'f8ec7': (0x8ec7, 0x8f70, [0x8d89, 0x8b5e, 0x8cbb, 0x8b37, 0x8af4, 0x8bea, 0x8c7c, 0x8abf]),
    'f8f70': (0x8f70, 0x902f, [0x8a7f, 0x8d89, 0x8c98, 0x8db0, 0x8ca1, 0x9b86, 0x8bea]),
    'f8227': (0x8227, 0x8270, [0x30, 0x40, 0x50, 0x60, 0x70, 0x80]),
    'f8273': (0x8273, 0x82c3, [0x8d89, 0x8ca1, 0x8c98, 0x8e81]),
    'f82c3': (0x82c3, 0x833f, [0x8d89, 0x8af4, 0x8b37, 0x8d3e, 0x8e3c, 0x902f, 0x8b4c, 0x8b0f, 0x8ec7, 0x8c98]),
    'f833f': (0x833f, 0x839f, [0x82c3, 0x8d89, 0x219c3, 0xd7b1, 0xda45]),
    'f839f': (0x839f, 0x83d9, [0x8d89, 0x8e56, 0x8ec7]),
    'f83d9': (0x83d9, 0x843e, [0x839f, 0x8d89, 0x219c3, 0xd7b1, 0xda45, 0x8c98]),
    'f843e': (0x843e, 0x84aa, [0x8abf, 0x8d89, 0x8cf3, 0x8e56, 0x8a7f, 0x8ec7, 0x8f70]),
    'f877f': (0x877f, 0x878d, [0xc0f6]),
    'f878d': (0x878d, 0x881b, [0x8d89, 0x8c98, 0x8d0b]),
    'f881b': (0x881b, 0x8917, [0x8d89, 0x8c98, 0x8d0b, 0x43b0, 0x219c3, 0x9e41, 0x9e55, 0x10ad2]),
    'f8917': (0x8917, 0x89f1, [0x9064, 0xa758, 0x8c98, 0xc4cd, 0xcaee, 0xd1ba]),
    'f89f1': (0x89f1, 0x8a54, [0x1eca0, 0x22e46, 0xbe2e]),
    'f8a54': (0x8a54, 0x8a7f, [0x12588, 0x4473]),
    'f8a7f': (0x8a7f, 0x8a9f, [0xd182]),
    'f8a9f': (0x8a9f, 0x8abf, [0x68bd]),
    'f8abf': (0x8abf, 0x8af4, [0x68bd]),
    'f8af4': (0x8af4, 0x8b0f, []),
    'f8b0f': (0x8b0f, 0x8b37, [0xa38a, 0x8120]),
    'f8b37': (0x8b37, 0x8b4c, [0x8b0f]),
    'f8b4c': (0x8b4c, 0x8b5e, [0xb5ab]),
    'f8bc7': (0x8bc7, 0x8bea, [0x9b86]),
    'f8bea': (0x8bea, 0x8c1e, [0x8c98, 0x960b]),
    'f8c47': (0x8c47, 0x8c7c, [0x8c98, 0x960b]),
    'f8c89': (0x8c89, 0x8c98, []),
    'f8cf3': (0x8cf3, 0x8d0b, [0x8bc7, 0x8cbb]),
    'f7fa7': (0x7fa7, 0x8120, [0x8120, 0x43b0, 0x73f5, 0x7c63, 0xbb26, 0x43b7, 0x802c]),
    'f902f': (0x902f, 0x9064, [0x9b86, 0x8c1e, 0x12406, 0x8c7c, 0x1243a]),
    'f81d9': (0x81d9, 0x8227, [0x813e, 0x6885, 0x43e5]),
    'fa10b': (0xa10b, 0xa1d1, []),
    'f9a8e': (0x9a8e, 0x9b0d, [0x4442, 0x43b7, 0x43b0, 0x8120]),
    'fda45': (0xda45, 0xda7f, []),
    'fd740': (0xd740, 0xd77a, [0x982b]),
    'fd77a': (0xd77a, 0xd7b1, [0x9e41, 0x9e55, 0x996f]),
    'f8659': (0x8659, 0x86b6, [0x8d89, 0xc439, 0x8f70]),
    'fbb8b': (0xbb8b, 0xbc15, [0x6885, 0xbadf]),
    'f6877': (0x6877, 0x68a0, []),
    'f6885': (0x6885, 0x68a0, []),
    'f68a0': (0x68a0, 0x68bd, []),
    'f68bd': (0x68bd, 0x68d8, []),
    'fc4cd': (0xc4cd, 0xc5d7, [0x68bd]),
    'fcaee': (0xcaee, 0xcb21, [0x43e5]),
    'fd12c': (0xd12c, 0xd157, []),
    'fd157': (0xd157, 0xd182, []),
    'fd182': (0xd182, 0xd19c, [0x30]),
    'fd19c': (0xd19c, 0xd1ba, [0x30]),
    'fc331': (0xc331, 0xc344, []),
    'fc344': (0xc344, 0xc35f, []),
    'fc35f': (0xc35f, 0xc439, []),
    'fc0f6': (0xc0f6, 0xc331, [0x8ca1, 0x9653, 0x43b0, 0xd363, 0xa758]),
    'fb9eb': (0xb9eb, 0xba2e, []),
    'fba2e': (0xba2e, 0xba60, []),
    'fbc15': (0xbc15, 0xbc2e, [0x43b0, 0xbb8e, 0x98db]),
    'fb5ab': (0xb5ab, 0xb5d8, [0x9e41]),
    'fb5d8': (0xb5d8, 0xb616, []),
    'fb616': (0xb616, 0xb6f1, []),
    'fbe2e': (0xbe2e, 0xbe6f, []),
    'fbe6f': (0xbe6f, 0xbe9d, [0x9e41, 0x9727]),
    'fcb21': (0xcb21, 0xcb77, [0xac63, 0xae3a, 0x43e5]),
    'fcb77': (0xcb77, 0xcc7b, [0xc4cd, 0xcaee, 0xcb21, 0xd1ba, 0x1eaed]),
    'fcc7b': (0xcc7b, 0xccc4, [0xc4cd, 0x1243a]),
    'fccc4': (0xccc4, 0xcccb, []),
    'fcccb': (0xcccb, 0xcd72, [0xa716, 0x9e41, 0xa6ae]),
    'fb319': (0xb319, 0xb394, [0xd1ba]),
    'fb6f1': (0xb6f1, 0xb765, [0xba2e, 0x6ec1]),
    'f6eb2': (0x6eb2, 0x6ec1, []),
    'f6ec1': (0x6ec1, 0x6f18, []),
    'fd2d1': (0xd2d1, 0xd363, []),
    'fs14b': (0x11dde, 0x11eb0, [0x21a00, 0xd7b1, 0xc344, 0xd157]),
    'fs187': (0x10ad2, 0x10b75, [0x9e41, 0x9e55, 0xac63]),
    'fs16b': (0x9064, 0x90e9, [0x9e41, 0xac63]),
    'fs18b': (0x11cc9, 0x11d2e, []),
    'f7a03': (0x7a03, 0x7a36, [0x6996]),
    'fae3a': (0xae3a, 0xae57, []),
    'fc439': (0xc439, 0xc4cd, []),
    'f4409': (0x4409, 0x4442, []),
    'fdb47': (0xdb47, 0xdbae, []),
    'fdbae': (0xdbae, 0xdc91, []),
    'fdc91': (0xdc91, 0xde0c, [0xa416, 0xd2d1, 0x43b0]),
    'fde0c': (0xde0c, 0xdec8, []),
    'f7012': (0x7012, 0x704f, []),
    'f704f': (0x704f, 0x7069, []),
    'f7069': (0x7069, 0x70b0, []),
    'f9bf9': (0x9bf9, 0x9c57, [0x9e7d, 0xc06e, 0xc0a4]),
    'f9c57': (0x9c57, 0x9d87, [0xdee4, 0xaab3, 0xc7ab, 0xfec6, 0x151e3, 0x9d87, 0x68d8, 0x10005, 0x10032]),
    'fa320': (0xa320, 0xa38a, []),
    'fa38a': (0xa38a, 0xa416, [0x43b0, 0x9e41, 0x7a36]),
    'fa416': (0xa416, 0xa444, [0x1416b]),
    'fa444': (0xa444, 0xa47e, [0x9e41, 0x1416b]),
    'fa47e': (0xa47e, 0xa4a9, [0x1416b]),
    'fa4a9': (0xa4a9, 0xa4c2, [0x1416b]),
    'fce9d': (0xce9d, 0xcf07, [0x9e41]),
    'fcf07': (0xcf07, 0xcf41, []),
    'fcf41': (0xcf41, 0xcf77, []),
    'f6377': (0x6377, 0x63cb, []),
    'f6361': (0x6361, 0x6377, []),
    'f63cb': (0x63cb, 0x63f7, []),
    'f63f7': (0x63f7, 0x640b, []),
    'f640b': (0x640b, 0x6423, []),
    'f6423': (0x6423, 0x643c, []),
    'f643c': (0x643c, 0x645b, []),
    'f645b': (0x645b, 0x64ad, []),
    'f66bb': (0x66bb, 0x66df, []),
    'f66df': (0x66df, 0x66e9, []),
    'f67bd': (0x67bd, 0x6877, []),
    'f67c0': (0x67c0, 0x6877, []),
    'f67d3': (0x67d3, 0x6877, []),
    'f12ae1': (0x12ae1, 0x12b29, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12b29': (0x12b29, 0x12ba3, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12ba3': (0x12ba3, 0x12bd8, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12bd8': (0x12bd8, 0x12bf2, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12bf2': (0x12bf2, 0x12bfb, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12bfb': (0x12bfb, 0x12c10, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12c10': (0x12c10, 0x12c48, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12c48': (0x12c48, 0x12c65, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12c65': (0x12c65, 0x12cb1, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12cb1': (0x12cb1, 0x12d08, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'f12d08': (0x12d08, 0x12e12, [0x8d89, 0x8af4, 0x8d3e, 0x8abf, 0x8c7c, 0x9688, 0x8120, 0x43b0, 0x9727, 0x98db, 0x1416b]),
    'faab3': (0xaab3, 0xac23, [0xabe9, 0x1ead8]),
    'fac23': (0xac23, 0xac63, [0x6c6d]),
    'f4580': (0x4580, 0x4870, [0x20232, 0x1f464, 0x4938, 0xb851]),
    'f4672': (0x4672, 0x4870, [0x20232, 0x1f464, 0x4938, 0xb851]),
    'f46f7': (0x46f7, 0x4870, [0x20232, 0x1f464, 0x4938, 0xb851]),
    'f478d': (0x478d, 0x4870, [0x20232, 0x1f464, 0x4938, 0xb851]),
    'f486d': (0x486d, 0x4938, [0x20232]),
    'f4938': (0x4938, 0x4965, []),
    'f4965': (0x4965, 0x4b03, [0x4b12, 0x200b5, 0xb616, 0x4b79, 0x4c46, 0x4b03, 0x6a73, 0x1416b]),
    'f4b03': (0x4b03, 0x4c46, [0x200b5, 0xb616, 0xba2e, 0x6a73, 0x20143, 0x200ab, 0x6acd, 0x205fb, 0x1f3e9, 0xdf7e, 0x65df, 0x1416b, 0xd101, 0x50f4, 0x4d50, 0x53d3, 0x52a6, 0x4e40]),
    'f4b12': (0x4b12, 0x4c46, [0x200b5, 0xb616, 0xba2e, 0x6a73, 0x20143, 0x200ab, 0x6acd, 0x205fb, 0x1f3e9, 0xdf7e, 0x65df, 0x1416b, 0xd101, 0x50f4, 0x4d50, 0x53d3, 0x52a6, 0x4e40]),
    'f4c46': (0x4c46, 0x4d41, [0x200b5, 0xb616, 0xba2e, 0x6a73, 0x20143, 0x200ab, 0x6acd, 0x205fb, 0x1f3e9, 0xdf7e, 0x65df, 0x1416b, 0xd101, 0x50f4, 0x4d50, 0x53d3, 0x52a6, 0x4e40]),
    'f4d41': (0x4d41, 0x4d50, []),
    'f4d50': (0x4d50, 0x4e27, [0x20232, 0x1f464, 0x4938, 0xee69, 0xedd5, 0x219c3, 0x1f306, 0x1416b]),
    'f4e27': (0x4e27, 0x4e40, []),
    'f4e40': (0x4e40, 0x4fce, [0x20232, 0x1f464, 0x4938, 0xee69, 0xedd5, 0x219c3, 0x1f306, 0x1416b]),
    'f4fce': (0x4fce, 0x500d, [0xae57, 0x20232, 0x1f62b, 0x4938, 0x1416b]),
    'f500d': (0x500d, 0x50e5, [0xae57, 0x20232, 0x1f62b, 0x4938, 0x1416b]),
    'f50e5': (0x50e5, 0x50f4, [0xae57, 0x20232, 0x1f62b, 0x4938, 0x1416b]),
    'f50f4': (0x50f4, 0x51ac, [0xae57, 0x20232, 0x1f62b, 0x4938, 0x1416b]),
    'f520b': (0x520b, 0x5297, [0xae57, 0x20232, 0x1f62b, 0x4938, 0x1416b]),
    'f5297': (0x5297, 0x52a6, [0xae57, 0x20232, 0x1f62b, 0x4938, 0x1416b]),
    'f52a6': (0x52a6, 0x538d, [0x20232, 0x1fb9c, 0x1f71a, 0xdee4, 0x4938, 0x1416b]),
    'f538d': (0x538d, 0x53a8, [0x20232, 0x1fb9c, 0x1f71a, 0xdee4, 0x4938, 0x1416b]),
    'f53a8': (0x53a8, 0x53c4, [0x20232, 0x1fb9c, 0x1f71a, 0xdee4, 0x4938, 0x1416b]),
    'f53c4': (0x53c4, 0x53d3, [0x20232, 0x1fb9c, 0x1f71a, 0xdee4, 0x4938, 0x1416b]),
    'f53d3': (0x53d3, 0x54b4, [0x20232, 0x1fb9c, 0x1f71a, 0xdee4, 0x4938, 0x1416b]),
    'f5679': (0x5679, 0x5716, [0x124fd, 0x124d8, 0x4938, 0x1416b]),
    'f5573': (0x5573, 0x5580, [0x4fce]),
    'f5580': (0x5580, 0x5593, [0x500d, 0x538d]),
    'f5593': (0x5593, 0x55b5, [0xdee4]),
    'f55b5': (0x55b5, 0x55d1, [0x4938]),
    'f55d1': (0x55d1, 0x55e0, []),
    'f55e0': (0x55e0, 0x566a, [0x124fd, 0x124d8, 0x4938, 0x1416b]),
    'f566a': (0x566a, 0x5679, []),
    'fdec8': (0xdec8, 0xdeda, []),
    'fdf2a': (0xdf2a, 0xdf7e, []),
    'fdf7e': (0xdf7e, 0xdfe5, []),
    'f5960': (0x5960, 0x5969, []),
    'f5af6': (0x5af6, 0x5b55, [0x672f, 0xdeda]),
    'f5b55': (0x5b55, 0x5c2e, [0x674f, 0x6c66, 0xb9eb, 0x5787, 0x66e9, 0x6eb2, 0x4938]),
    'f5cc2': (0x5cc2, 0x5cd9, [0x674f, 0x1416b]),
    'f5da5': (0x5da5, 0x5df6, [0xe5e6, 0xe070, 0xf791, 0x4938]),
    'f5df6': (0x5df6, 0x5e1c, [0x20232, 0x672f]),
    'f5e1c': (0x5e1c, 0x5f0a, [0x20232, 0x674f, 0xdee4]),
    'f5f0a': (0x5f0a, 0x5fc5, []),
    'f5fc5': (0x5fc5, 0x6057, []),
    'f6057': (0x6057, 0x608e, [0x20232, 0xb3b6]),
    'f608e': (0x608e, 0x612b, [0xe03c, 0xe571, 0xe5ab]),
    'f612b': (0x612b, 0x6142, [0xe5ab, 0xdee4]),
    'f6142': (0x6142, 0x616f, [0xe5ab]),
    'f616f': (0x616f, 0x6267, [0xe03c, 0xe5ab, 0x4d41, 0x4e27, 0x4fce, 0x50e5, 0x51ac, 0x5297, 0x538d, 0x53c4, 0x54b4, 0x5593, 0x4938, 0xb8a2, 0xe571]),
    'f6267': (0x6267, 0x6294, [0xe571]),
    'f6294': (0x6294, 0x6361, [0xe03c, 0xe571, 0xe5ab, 0x5573]),
    'f64ad': (0x64ad, 0x6562, []),
    'f6562': (0x6562, 0x658a, [0xe5ab]),
    'f658a': (0x658a, 0x65df, [0xe02a, 0x1df08, 0xdee4]),
    'f65df': (0x65df, 0x66bb, [0xe5f6, 0xe5ab, 0x6acd, 0xba60, 0xdfe5]),
    'f66e9': (0x66e9, 0x670f, [0x1df08]),
    'f670f': (0x670f, 0x672f, [0x1df08]),
    'f672f': (0x672f, 0x674f, [0x1df08]),
    'f674f': (0x674f, 0x677f, [0x3eea, 0x6fcc]),
    'f6996': (0x6996, 0x69fe, []),
    'f69fe': (0x69fe, 0x6a73, []),
    'f6a73': (0x6a73, 0x6acd, [0x670f, 0x6eb2, 0xdee4, 0xd182]),
    'f6acd': (0x6acd, 0x6b14, [0x670f, 0xdee4]),
    'f5969': (0x5969, 0x59c8, []),
    'f59de': (0x59de, 0x5af6, [0xdee4, 0x1416b]),
    'f42bb': (0x42bb, 0x43b0, [0x30]),
    'f4473': (0x4473, 0x44c0, []),
    'f6ba3': (0x6ba3, 0x6c04, []),
    'f677f': (0x677f, 0x67bd, [0x3eea]),
    'f6d4a': (0x6d4a, 0x6def, [0x3eea]),
    'f6c66': (0x6c66, 0x6cd6, [0x3eea]),
    'f6cd6': (0x6cd6, 0x6d4a, [0x3eea]),
    'f6c04': (0x6c04, 0x6c66, []),
    'fbe9d': (0xbe9d, 0xbea7, [0xdee4]),
    'fbea7': (0xbea7, 0xbebb, []),
    'fbebb': (0xbebb, 0xbed7, []),
    'fbed7': (0xbed7, 0xbee6, []),
    'fbee6': (0xbee6, 0xbf07, []),
    'fbf07': (0xbf07, 0xbf55, [0x1e133, 0x1e098]),
    'fbf55': (0xbf55, 0xbfc9, []),
    'f11b4e': (0x11b4e, 0x11bdf, [0x9e41, 0x9e55, 0xcfac]),
    'fe65b': (0xe65b, 0xe717, [0xead8]),
    'fe571': (0xe571, 0xe5ab, []),
    'fe5ab': (0xe5ab, 0xe5e6, []),
    'fe5e6': (0xe5e6, 0xe5f6, []),
    'fe5f6': (0xe5f6, 0xe621, []),
    'fe621': (0xe621, 0xe636, []),
    'fe636': (0xe636, 0xe65a, []),
    'fdee4': (0xdee4, 0xdf2a, [0x30]),
    'fdfe5': (0xdfe5, 0xe02a, []),
    'fe02a': (0xe02a, 0xe03c, []),
    'fe03c': (0xe03c, 0xe055, []),
    'fe055': (0xe055, 0xe070, []),
    'fe070': (0xe070, 0xe08b, []),
    'ff629': (0xf629, 0xf781, [0xead8, 0x1416b, 0x9e69]),
    'f5ce': (0xf5ce, 0xf5f2, [0xf629]),
    'f5f2': (0xf5f2, 0xf629, []),
    'f781': (0xf781, 0xf791, [0xe055]),
    'f791': (0xf791, 0xf7a1, [0xe070]),
    'f7a1': (0xf7a1, 0xf7d0, [0xe5f6, 0xe5ab]),
    'f7d0': (0xf7d0, 0xf862, [0xee69, 0xe5f6, 0xe5ab, 0x1ead8, 0xdee4]),
    'f862': (0xf862, 0xf8c9, [0xe5f6, 0xe5ab, 0xee69, 0xedd5]),
    'f8c9': (0xf8c9, 0xf8e1, []),
    'f44c0': (0x44c0, 0x4580, [0xedd5, 0xe559, 0x486d, 0x4580]),
    'f84aa': (0x84aa, 0x8659, [0x8d89, 0x8c98, 0x8ca1, 0xc331, 0x8c47, 0x8af4, 0x8c89, 0xcfac, 0x123d3, 0xd101, 0x8b0f, 0xc35f]),
}
HEADING = [0, -4, 1, -4, 2, -4, 3, -4, 4, -4, 4, -3, 4, -2, 4, -1, 4, 0, 4, 1, 4, 2, 4, 3, 4, 4, 3, 4, 2, 4, 1, 4, 0, 4, -1, 4, -2, 4, -3, 4, -4, 4, -4, 3, -4, 2, -4, 1, -4, 0, -4, -1, -4, -2, -4, -3, -4, -4, -3, -4, -2, -4, -1, -4]
POOLREC, OBJ1, OBJ2, TMPL = 0x800, 0x900, 0xa00, 0xc00
DS_WINDOWS = [(0x19c0, 0x40), (0x1c30, 0x50), (0x4160, 0x10), (0x9080, 0x10), (0x1978, 0x48), (0x1c80, 0x50), (0x1ec0, 0x10), (0x1960, 0x18), (0x9090, 0x10), (0x1904, 0x10), (0x33c, 0x100), (0x640, 0xc), (0x1940, 0x20)]   # filled with pseudo-random bytes (the globals of the functions that read them)


class ActionOracle(CombatOracle):
    RANGES = {}

    RET = None

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP if self.RET is None else self.RET))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)


def prng(seed, n):
    """The same linear congruential generator is used by tests/OriginalUnitActionChecks.cs."""
    x = seed & 0xffffffff; out = bytearray()
    for _ in range(n):
        x = (x * 1664525 + 1013904223) & 0xffffffff; out.append(x >> 24)
    return bytes(out)


def constrain(b):
    """The army (+0) of an object is 0..2 and its template (+0E) 0: the lookups 6885 / 68BD then stay inside the one template buffer."""
    b = bytearray(b); b[0] %= 3; b[0xe] = 0
    for k in (4, 8): b[k + 1] &= 3; b[k + 2] = 0; b[k + 3] = 0
    return b


def cases(names):
    rng = random.Random(0x8e3c)
    out = []
    for n in range(2500):
        fn_pick = rng.choice(names + ['fd2d1'] * 4 + ['fb319'] * 4 + ['fc0f6'] * 5 + ['fcb77'] * 4 + ['fcccb'] * 4 + ['fdb47'] * 5 + ['fdbae'] * 8 + ['fdc91'] * 14 + ['fde0c'] * 8 + ['f4409'] * 2 + ['f5cc2'] * 3 + ['f5da5'] * 10 + ['f5df6'] * 6 + ['f5e1c'] * 24 + ['f5f0a'] * 16 + ['f5fc5'] * 12 + ['f6057'] * 10 + ['f608e'] * 18 + ['f612b'] * 4 + ['f6142'] * 10 + ['f616f'] * 30 + ['f6267'] * 10 + ['f6294'] * 30 + ['f64ad'] * 30 + ['f6562'] * 6 + ['f658a'] * 10 + ['f65df'] * 30 + ['f66e9'] * 3 + ['f670f'] * 3 + ['f672f'] * 3 + ['f674f'] * 4 + ['f6996'] * 40 + ['f69fe'] * 40 + ['f6a73'] * 8 + ['f6acd'] * 4 + ['f5969'] * 20 + ['f59de'] * 30 + ['f42bb'] * 40 + ['f4473'] * 40 + ['f6ba3'] * 40 + ['f677f'] * 3 + ['f6d4a'] * 40 + ['f6c66'] * 40 + ['f6cd6'] * 30 + ['f6c04'] * 40 + ['fbe9d'] * 2 + ['fbea7'] * 20 + ['fbebb'] * 20 + ['fbed7'] * 10 + ['fbee6'] * 20 + ['fbf07'] * 12 + ['fbf55'] * 50 + ['f11b4e'] * 40 + ['fe65b'] * 40 + ['fe571'] * 40 + ['fe5ab'] * 30 + ['fe5e6'] * 6 + ['fe5f6'] * 20 + ['fe621'] * 20 + ['fe636'] * 24 + ['fdee4'] * 40 + ['fdfe5'] * 30 + ['fe02a'] * 20 + ['fe03c'] * 12 + ['fe055'] * 12 + ['fe070'] * 12 + ['ff629'] * 40 + ['f5ce'] * 24 + ['f5f2'] * 30 + ['f781'] * 2 + ['f791'] * 2 + ['f7a1'] * 20 + ['f7d0'] * 40 + ['f862'] * 30 + ['f8c9'] * 10 + ['f44c0'] * 30 + ['f5960'] * 2 + ['f5af6'] * 12 + ['f5b55'] * 16 + ['fdec8'] * 2 + ['fdf2a'] * 10 + ['fdf7e'] * 8 + ['f5679'] * 12 + ['f5573'] * 4 + ['f5580'] * 8 + ['f5593'] * 6 + ['f55b5'] * 6 + ['f55d1'] * 2 + ['f55e0'] * 14 + ['f566a'] * 2 + ['f52a6'] * 14 + ['f538d'] * 2 + ['f53a8'] * 4 + ['f53c4'] * 2 + ['f53d3'] * 12 + ['f4fce'] * 4 + ['f500d'] * 14 + ['f50e5'] * 2 + ['f50f4'] * 10 + ['f520b'] * 8 + ['f5297'] * 2 + ['f4d50'] * 12 + ['f4e27'] * 2 + ['f4e40'] * 16 + ['f4b03'] * 6 + ['f4b12'] * 14 + ['f4c46'] * 16 + ['f4d41'] * 2 + ['f4965'] * 22 + ['f486d'] * 14 + ['f4938'] * 3 + ['f4580'] * 14 + ['f4672'] * 6 + ['f46f7'] * 8 + ['f478d'] * 8 + ['faab3'] * 10 + ['fac23'] * 6 + ['f12ae1'] * 6 + ['f12b29'] * 6 + ['f12ba3'] * 6 + ['f12bd8'] * 6 + ['f12bf2'] * 6 + ['f12bfb'] * 6 + ['f12c10'] * 6 + ['f12c48'] * 6 + ['f12c65'] * 6 + ['f12cb1'] * 6 + ['f12d08'] * 6 + ['f6377'] * 5 + ['f6361'] * 5 + ['f63cb'] * 5 + ['f63f7'] * 5 + ['f640b'] * 5 + ['f6423'] * 5 + ['f643c'] * 5 + ['f645b'] * 5 + ['f66bb'] * 5 + ['f66df'] * 5 + ['f67bd'] * 5 + ['f67c0'] * 5 + ['f67d3'] * 5 + ['fce9d'] * 6 + ['fcf07'] * 6 + ['fcf41'] * 8 + ['fa320'] * 6 + ['fa38a'] * 8 + ['fa416'] * 4 + ['fa444'] * 8 + ['fa47e'] * 3 + ['fa4a9'] * 3 + ['f9bf9'] * 3 + ['f9c57'] * 14 + ['f7012'] * 6 + ['f704f'] * 4 + ['f7069'] * 8)
        out.append({'fn': names[n] if n < len(names) else fn_pick, 'r': rng.randrange(1 << 32), 'o1': rng.randrange(1 << 32), 'o2': rng.randrange(1 << 32), 'tm': rng.randrange(1 << 32), 'f1c': rng.choice([0, 0x900, 0xa00]), 'f14': rng.choice([0, 1, 2, 3, 4, 5, 0x40, 0x41, 0x4a, 0x2d, (rng.randrange(256) & ~7) | rng.randrange(6)]), 'f19': rng.choice([0, 1, 5, 0x80, 0xff, rng.randrange(256)]), 'pt': rng.randrange(1 << 32), 'f3': rng.randrange(8), 'nch': rng.randrange(0, 5), 'st': rng.randrange(0, 5), 'bm': rng.choice([8, 0x1000, 0x20000, 0xffffffff, 0x10, rng.randrange(1 << 32)]), 'sel': rng.randrange(4), 'g273': rng.randrange(1, 5), 'g274': rng.randrange(1, 5), 'ds': rng.randrange(1 << 32), 'tab': rng.randrange(1 << 32), 'fs13d': rng.randrange(256), 'px': rng.randrange(1024), 'py': rng.randrange(1024), 'cur': rng.choice([0xffffffff, rng.randrange(0x3ff), rng.randrange(0x3ff), 0]), 'g525': rng.randrange(256), 'g526': rng.randrange(256), 'g1c61': rng.choice([0, 1, 5, rng.randrange(256)]), 'g2730': rng.choice([0xffffffff, 0, rng.randrange(1 << 32)]), 'rt': rng.randrange(1 << 32), 'map': rng.randrange(1 << 32), 'f15': rng.choice([0, 1, 2, 3, 0x10, 0x11, rng.randrange(256)]), 'f31': rng.choice([0, 1, rng.randrange(256)]),
                    'f30': rng.choice([0, 0x10, 0x91, 0xf3, rng.randrange(256)]), 'f17': rng.choice([0, 2, 0x40, 0x42, rng.randrange(1 << 16)]), 'g1c9f': rng.choice([0, 0x40, 0x80, rng.randrange(256)]), 'g512': rng.choice([0, 0, 1, rng.randrange(1 << 20)]),
                    'g51a': rng.randrange(1 << 20), 'g51e': rng.choice([0, 0x400900]), 'g522': rng.choice([0, 3, rng.randrange(256)]),
                    'regs': [rng.choice([0, 1, 2, 3, rng.randrange(1 << 32)]) for _ in range(5)],
                    'calls': [[rng.choice([0, 1, 1]), rng.choice([0, 1, 2, 3, rng.randrange(256)]) | (rng.choice([0, 1, 2, 3, 4]) << 8), rng.randrange(1 << 20) if rng.random() < 0.4 else rng.choice([0, 1, 2, 3, 0xff]), rng.randrange(1 << 20) if rng.random() < 0.5 else rng.choice([0, 1, 2]), rng.randrange(1 << 20), rng.choice([0, 0x400900, 0x400a00])] for _ in range(8)]})
    for n, c in enumerate(out):
        if c['fn'] == 'fcccb' and n % 2 == 0: c['z19'] = 1; c['f15'] = 1; c['f22'] = (n // 2) % 5; c['f23'] = (n // 3) % 4; c['f24'] = (n // 5) % 40
        if c['fn'] == 'fs187': c['regs'][2] = n * 37 % 0x400; c['regs'][3] = n * 53 % 0x400
        if c['fn'] in ('fdb47', 'fdbae', 'fdc91', 'f4409'): c['px'] = 160 + n * 7 % 340; c['py'] = 160 + n * 13 % 340
        if c['fn'] == 'fdc91':
            c['f22'] = [0, 1, 3, 0xfc, 0x63, 0x64, 0xff, 4, 0x20, 0x2f][n % 10]; c['f15'] = [0, 0, 1, 1, 1, 2, 0x12, 0x22, 0x32, 3, 0x13, 0x41][(n // 10) % 12]; c['sel'] = (n // 120) % 4
        if c['fn'] == 'fde0c':
            r2, r3 = c['regs'][2], c['regs'][3]; c['regs'][1] = 1 + (n % 2) + ((1 + (n // 2) % 2) << 8); c['regs'][2] = 32 * (3 + n % 12) + 16; c['regs'][3] = 32 * (3 + (n // 3) % 12) + 16; c['sel'] = (n // 7) % 4
        if c['fn'] in ('f7012', 'f704f', 'f7069'):
            toks = bytearray()
            for _ in range(rng.randrange(0, 18)):
                kind = rng.randrange(8)
                if kind == 0: toks += b'\x0d'
                elif kind == 1: toks += b'\x7e' + bytes([rng.randrange(0x20, 0xe0)])
                elif kind == 2 and c['fn'] == 'f7069': toks += b'%n' + bytes([0x31 + rng.randrange(4)])
                else: toks += bytes([rng.choice([rng.randrange(0x20, 0x7d), rng.randrange(0xb0, 0xff)])])
            c['txt'] = (bytes(toks) + b'\xff').hex(); c['font'] = bytes(rng.randrange(256) for _ in range(0x200 + 32)).hex()
            c['regs'][0] = (c['regs'][0] & ~0xff) | (c['regs'][0] % 8); c['regs'][1] = (c['regs'][1] & ~0xff) | (c['regs'][1] % 8); c['regs'][2] = (c['regs'][2] & ~0xff) | (c['regs'][2] % 8); c['regs'][4] = 0x400000 + 0x2800
        if c['fn'] in ('f9bf9', 'f9c57'):
            c['w35'] = [0x44, 0x58, 0xf, 0x3b, 0x3c, 0x3d, 2, 3, 5, 9, 10, rng.randrange(1 << 16), 0x44, 0x3c][n % 14]; c['d1cad'] = [1, 0x400, 0x401, 0xffffffff, 0, 1, 1, rng.randrange(1 << 32)][(n // 14) % 8]
            c['b1c4e'] = [1, 1, 1, 0, 7][(n // 5) % 5]; c['d1ca9'] = rng.choice([0, 0x1000, 0xfff, 0x2000, rng.randrange(1 << 32)]) & 0xffffffff; c['b19e9'] = rng.choice([0, 1, 0xff]); c['b9090'] = rng.choice([0, 1, 1]); c['b9091'] = rng.randrange(256)
            ids = rng.sample(range(1, 9), rng.randrange(2, 6)); ids += [5] if max(ids) <= 3 else []; c['lst'] = bytes(ids + [0xff]).hex(); c['b1972'] = rng.choice(ids); c['d1cb1'] = rng.randrange(1 << 32); c['ff'] = rng.randrange(1 << 8)
            if n % 3 == 0: c['calls'][0][1] = 0x1d001f
        if c['fn'] == 'fa320': c['regs'][0] = rng.choice([0, 3, 7, 8, 20, 25, 31, 32, 40, rng.randrange(1 << 32)]); c['regs'][1] = rng.choice([0, 3, 7, 8, 20, 25, 31, 32, 40, rng.randrange(1 << 32)]); c['sel'] = n % 3
        if c['fn'] == 'fa38a':
            for call in c['calls']: call[1] = (call[1] & ~0xff) | (call[1] % 8); call[3] = rng.choice([0, 0x402000, 0x402000])
            c['sel'] = n % 3
        if c['fn'] in ('fa416', 'fa444', 'fa4a9'): c['regs'][0] = rng.choice([0, 1, 5, rng.randrange(1 << 32)]); c['regs'][1] = rng.choice([0, 0xff, 0x1, 0x80, rng.randrange(1 << 32)])
        if c['fn'] in ('fce9d', 'fcf07', 'fcf41'):
            c['sel'] = n % 4; seg = bytearray(prng(c['map'] + 3, 8192))
            if c['sel'] & 2:
                for t in range(1024): seg[t * 8 + 2] = seg[t * 8 + 3] = ((seg[t * 8 + 2] & 15) * 17 if c['sel'] & 1 == 0 else seg[t * 8 + 2])
            t = rng.randrange(1024); desc = struct.unpack_from('<I', seg, t * 8)[0]
            if c['fn'] == 'fce9d':
                c['regs'][0] = desc if n % 5 else rng.randrange(1 << 32)
                for call in c['calls']: call[1] = call[1] % 256
            elif c['fn'] == 'fcf07':
                c['regs'][1] = rng.choice([0x7, 0xf00, 0xffff0f00, 0xff, rng.randrange(1 << 32), 0xffffffff]); c['regs'][0] = desc if n % 4 else rng.randrange(1 << 32)
            else:
                c['regs'][0] = ((desc >> 8 & 0xff) << 8) | (desc & 7) if n % 3 else rng.randrange(1 << 32)
                if c['sel'] & 2 and n % 3: c['regs'][0] = ((seg[t * 8 + 1]) << 8) | (seg[t * 8] & 7)
        if c['fn'] in ('f6377', 'f6361', 'f63cb', 'f63f7', 'f640b', 'f6423', 'f643c', 'f645b', 'f66bb', 'f66df', 'f67bd', 'f67c0', 'f67d3', 'f616f', 'f6267', 'f6294', 'f64ad', 'f6562', 'f658a', 'f65df'):
            c['sel'] = n % 4; c['wi'] = bytes(rng.randrange(256) for _ in range(0x30)).hex()
            if c['fn'] in ('f67bd', 'f67d3'): c['regs'][0] = rng.randrange(32); c['regs'][1] = rng.randrange(32)
            if c['fn'] == 'f67c0': c['regs'][0] = rng.randrange(14 * 32); c['regs'][1] = rng.randrange(14 * 32)
            c['cad'] = [0, 31, 32, 33, 100, 0x400, 0x1fff, rng.randrange(0x2000)][n % 8]
        if c['fn'] in ('f12ae1', 'f12b29', 'f12ba3', 'f12bd8', 'f12bf2', 'f12bfb', 'f12c10', 'f12c48', 'f12c65', 'f12cb1', 'f12d08'):
            c['sel'] = n % 4; c['regs'][4] = rng.choice([0x400900, 0x400a00])
            if c['fn'] == 'f12d08': c['f19'] = rng.choice([0, 1, 0xf9, 0xfa, 0xfb, 0xfc, 0x80, 0xff, 7]); c['f17'] = rng.choice([0, 0x80, 0x40, 0x82])
            for call in c['calls']: call[5] = rng.choice([0x400900, 0x400a00]); call[1] = call[1] if rng.random() < 0.5 else (call[1] & ~0xff) | rng.choice([0, 1, 2, 5]); call[4] = (call[4] & ~0xff) | rng.choice([0, 4, 6, 3, 7])
        if c['fn'] in ('faab3', 'fac23'):
            c['sel'] = n % 8; c['regs'][0] = rng.choice([0, 5, 9, 10, 99, 100, 12345, 1000000000, 0xffffffff, rng.randrange(1 << 32)]) if c['fn'] == 'fac23' else c['regs'][0]
            c['deltas'] = [rng.choice([0, 1, -1, 5, -5, 999, 1000, -999, -1000, rng.randrange(-5000, 5000)]) for _ in range(3)]
        if c['fn'] in ('f4580', 'f4672', 'f46f7', 'f478d', 'f486d', 'f4938', 'f4965', 'f4b03', 'f4b12', 'f4c46', 'f4d41', 'f4d50', 'f4e27', 'f4e40', 'f4fce', 'f500d', 'f50e5', 'f50f4', 'f520b', 'f5297', 'f52a6', 'f538d', 'f53a8', 'f53c4', 'f53d3', 'f5679', 'f5573', 'f5580', 'f5593', 'f55b5', 'f55d1', 'f55e0', 'f566a'):
            c['sel'] = n % 4
            if c['fn'] in ('f4b03', 'f4b12', 'f4c46', 'f4d41', 'f4d50', 'f4e27', 'f4e40', 'f4fce', 'f500d', 'f50e5', 'f50f4', 'f520b', 'f5297', 'f52a6', 'f538d', 'f53a8', 'f53c4', 'f53d3', 'f5679', 'f5573', 'f5580', 'f5593', 'f55b5', 'f55d1', 'f55e0', 'f566a'):
                for call in c['calls']: call[5] = random.Random(n).choice([0x400900, 0x400a00]); call[1] = random.Random(n + 1).choice([0, 1, 2, 3, call[1]])
            c['cu'] = {'c60': rng.choice([1, 1, 1, 0]), 'b193a': rng.randrange(2), 'b194b': rng.randrange(2), 'l0': rng.choice([0, 0, 0x2a, 5]), 'mx': rng.randrange(0, 40), 'my': rng.randrange(0, 40), 'vx': rng.randrange(0, 8), 'vy': rng.randrange(0, 8),
                        'own': rng.randrange(8), 'b1971': rng.randrange(8), 'g89': rng.choice([0, 0, 1]), 'b87': rng.randrange(256), 'b86': rng.choice([0, 1]), 'b8a': rng.choice([0, 1, 2]), 'o19c0': rng.choice([0x48, 0x50, 0x58, rng.randrange(256)]),
                        'l12': rng.choice([0, 1, 2, 3]), 'l13': rng.choice([1, 3, 9, 0x11, 5, 0x0d, 0, 0x19, 8, 0xb]), 'l5': rng.choice([0, 0, 1, 2]), 'ld': rng.choice([0, 1, 3, 8, 0xb, 7, 2]), 'ed20': rng.randrange(1 << 20), 'l2': rng.randrange(8), 'px': rng.choice([0x1bf, 0x1c0 - 1, rng.randrange(0, 0x1c0)]), 'py': rng.choice([0x1bf, 0x1c0 - 1, rng.randrange(0, 0x1c0)]), 'hp': rng.choice([0, 1, 0x80, 0x7f, 0xff]), 'l0b': rng.choice([0, 0, 0x28, 3]), 'c86': rng.choice([0, 1, 2, 0]), 'k35': rng.choice([0x39, 0x1c, 0x1d, 1, 0, 5]), 'p74': rng.choice([0, 1, 2]), 'l3': rng.choice([0, 0, 1, 0x80, 0xff, 2]), 'd55': rng.randrange(4), 'e0': rng.choice([0, 1]), 'v99e': rng.choice([0, 1]), 'v9a2': rng.choice([4, 6, 0, 3]), 'v9aa': rng.choice([0, 1]), 'v9ae': rng.choice([8, 0, 2]), 'v92': rng.choice([0, 1]), 'v96': rng.choice([2, 0, 1]), 'c4c': rng.choice([0, 0xe, 0xf, 0x10, 3]), 'bx': rng.choice([0, 1, 9, 0x11, 3, 8, 0x19, 0x10, 2, 0xb]), 'l5b': rng.choice([0, 1, 2]), 'v27f4': rng.choice([0, 1, 1]), 'a4f0': rng.choice([0, 0, 5, 0x123]), 'a4f4': rng.randrange(1 << 20), 'a4f8': rng.randrange(1 << 20), 'a4fc': rng.randrange(1 << 20)}
            c['regs'][2] = rng.randrange(1 << 32); c['regs'][3] = rng.randrange(1 << 32)
            if c['fn'] in ('f4e40', 'f4d50', 'f478d', 'f46f7', 'f52a6', 'f53d3', 'f5679', 'f5573', 'f5580', 'f5593', 'f55b5', 'f55d1', 'f55e0', 'f566a') and n % 3 == 0: c['cu']['ld'] = 0x11 if c['fn'] == 'f52a6' else 0x12; c['cu']['bx'] = 9; c['cu']['l13'] = 9; c['cu']['l5b'] = 2 if c['sel'] & 1 else 1; c['cu']['l5'] = c['cu']['l5b']; c['cu']['e0'] = 0; c['cu']['g89'] = 1 if n % 6 == 0 else 0; c['cu']['b1971'] = 9
        if c['fn'] in ('fdec8', 'fdf2a', 'fdf7e', 'f5cc2', 'f5da5', 'f5df6', 'f5e1c', 'f5f0a', 'f5fc5', 'f6057', 'f608e', 'f612b', 'f6142', 'f7a1', 'f7d0', 'f862', 'f8c9', 'fe5e6', 'fe621', 'fe636', 'fbf07', 'fbf55', 'f5593', 'f6a73', 'f6acd', 'f6996', 'f69fe', 'f5969', 'f59de', 'f42bb', 'f4473', 'f44c0', 'f5ce', 'f5f2', 'f781', 'f791', 'fdfe5', 'fe02a', 'fe03c', 'fe055', 'fe070', 'fdee4', 'fe65b', 'f6ba3', 'f6c04', 'f6c66', 'f6cd6', 'f677f', 'f6d4a'): c['f3'] = 5 + c['f3'] % 3
        if c['fn'] in ('f5960', 'f5af6', 'f5b55'):
            c['sel'] = n % 4; c['wi'] = bytes(rng.randrange(256) for _ in range(0x30)).hex(); c['hp'] = rng.choice([0, 1, 100, 0x80])
            c['k1d'] = rng.choice([0, 1, 2, 5, 0xff, 3]); c['k1c'] = rng.choice([0xff, 0xff, 0, 1, 2, 5, 3]); c['t12'] = rng.choice([0, 1, 2, 5]); c['b19e7'] = rng.choice([0, 1, 1]); c['own'] = 5 + rng.randrange(3); c['qid'] = rng.choice([0x29, 0x2b, 0x2c, 0x32, 0x44]); c['qn'] = rng.choice([0, 1, 1, 2]); c['l0q'] = rng.choice([0, 0x29, 0x2b, 0x2c, 0x32])
            toks = bytearray(bytes([rng.randrange(0x20, 0x7d) for _ in range(rng.randrange(0, 12))]))
            c['txt'] = (bytes(toks) + b'\xff').hex(); c['font'] = bytes(rng.randrange(256) for _ in range(0x200 + 32)).hex(); c['regs'][4] = 0x400800
        if c['fn'] in ('f5cc2', 'f5da5', 'f5df6'):
            c['sel'] = n % 4; c['hp'] = rng.choice([0, 1, 50, 100, 0x80]); c['k35'] = rng.choice([1, 0, 5]); c['d193a'] = rng.choice([0, 2, 1, 3]); c['ed20'] = rng.randrange(1 << 20); c['b87'] = rng.randrange(256); c['qid'] = rng.choice([0x29, 0x2b, 0x2c, 0x32, 0x44]); c['qn'] = rng.choice([0, 1, 1, 2]); c['l0q'] = rng.choice([0, 0x29, 0x2b, 0x2c, 0x32])
            c['regs'][4] = 0x400900
            for call in c['calls']: call[5] = 0x400900
        if c['fn'] in ('f5e1c', 'f5f0a', 'f5fc5', 'f6057', 'f608e', 'f612b', 'f6142', 'f7a1', 'f7d0', 'f862', 'f8c9', 'fe5e6', 'fe621', 'fe636', 'fbf07', 'fbf55'):
            c['k35'] = rng.choice([1, 0, 5, 0, 0]); c['b19e7'] = rng.choice([0, 1, 2, 3, 3, 1]); c['d1951'] = rng.choice([0, 0x46, 0x47, 0x19a, 0x19b, 0x100, rng.randrange(1 << 12)])
            c['wf'] = rng.choice([5, 6, 7, 8, 9, 0xa, 0xd, 0xe, 0x12, 0x15, 0x19, 0x1a, 5, 7]); c['w1a'] = rng.choice([0, 0x1e, 0x1f, 0x20, 5, 0x100, 0x1a2, 0x1a1, 0xff00, 0x8000, 0x7fff, rng.randrange(65536)])
            c['k1c'] = rng.choice([1, 2, 3, 3]); c['k1d'] = rng.choice([0, 1, 7, 8, 0xf9, 0xfa, 0xf8, 0xff, 2, 0x80, 6]); c['m16'] = rng.choice([0, 1, 1]); c['t22'] = rng.randrange(1 << 32); c['r16'] = rng.randrange(1 << 32)
            if n % 3 == 0: c['k1d'] = rng.choice([0xf9, 0xfa, 0xf8, 5]); c['d1951'] = rng.choice([0x19a, 0x19b]); c['w1a'] = (0x1a2 - 0x36 * c['k1c'] + rng.choice([-1, 0, 0, 1])) & 0xffff; c['b19e7'] |= 1; c['wf'] = rng.choice([5, 7, 9, 0xd, 0xf]); c['d1951'] = rng.choice([0x19a, 0x19b, 0x199, 0x46, 0x47, 0x45])
            if n % 3 == 1: c['w1a'] = rng.choice([0x1e, 0x1f, 0x1d]); c['b19e7'] |= 1; c['wf'] = rng.choice([5, 7, 9, 0xd, 0xf]); c['d1951'] = rng.choice([0x46, 0x47, 0x45, 0x19a])
            c['b87'] = rng.randrange(256); c['qid'] = rng.choice([0x29, 0x2b, 0x2c, 0x32, 0x33, 0x44]); c['qn'] = rng.choice([0, 1, 1, 2, 3]); c['l0q'] = rng.choice([0, 0x29, 0x2b, 0x2c, 0x33]); c['tail'] = rng.randrange(1 << 20); c['regs'][4] = 0x400800
            c['own'] = 5 + rng.randrange(3)
            c['pos'] = rng.choice([0x180, 0x184, 0x185, 0x186, 0x187, 0x190, 0xfffffff0, rng.randrange(1 << 12)])
            if c['fn'] in ('f608e', 'f612b', 'f6142', 'f7a1', 'f7d0', 'f862', 'f8c9', 'fe5e6', 'fe621', 'fe636', 'fbf07', 'fbf55'):
                for call in c['calls']: call[5] = 0x400900
            if c['fn'] in ('f5f0a', 'f5fc5'): c['b87'] = c['regs'][1] & 0xff
            if c['fn'] in ('f5f0a', 'f5fc5'): c['w1a'] = rng.choice([0x1f, 0x3f, 0x5f, 0x1f, 0x7f, 0x1e, 0x20, 0xff])
        if c['fn'] in ('f616f', 'f6267', 'f6294', 'f64ad', 'f6562', 'f658a', 'f65df'):
            for call in c['calls']: call[5] = 0x400900
        if c['fn'] == 'f616f':
            wb = bytes.fromhex(c['wi']); sla = 0x1978 + 12 * (wb[2] % 6)
            if (wb[3] % 8) & 1: c['calls'][1][5] = sla
            else: c['calls'][0][5] = sla
        if c['fn'] in ('f66e9', 'f670f', 'f672f', 'f674f'): c['u22'] = rng.randrange(1 << 32); c['u2a'] = rng.randrange(1 << 32)
        if c['fn'] in ('f6996', 'f69fe'):
            m = rng.choice([0x40, 0x300, 0x3000, 0x1000])
            c['regs'][0] = rng.randrange(m); c['regs'][1] = rng.randrange(m); c['regs'][2] = rng.randrange(m); c['regs'][3] = rng.randrange(m)
            if n % 5 == 0: c['regs'][2] = c['regs'][0] + rng.choice([0, 3, 40]); c['regs'][3] = c['regs'][1] + rng.choice([0, 3, 40])
            if n % 7 == 0: c['regs'][2] = c['regs'][0] - rng.choice([0, 1, 9]); c['regs'][3] = c['regs'][1] + rng.choice([0, 1, 9, -5])
            c['regs'] = [max(x, 0) & 0xffffffff for x in c['regs']]
            c['s3'] = rng.randrange(8); c['s4'] = rng.randrange(8); c['tab'] = rng.randrange(1 << 32)
            if c['fn'] == 'f69fe': c['regs'][4] = 0x400900
            if c['fn'] == 'f69fe' and n % 4 in (0, 1):
                if n % 4 == 0: da = (c['s3'] << 5) + rng.choice([-1, 0, 1]); db = rng.randrange(0, 40)
                else: da = rng.randrange(0, 4); db = (c['s4'] << 5) + rng.choice([-1, 0, 1])
                da = max(da, 0); db = max(db, 0); c['regs'][2] = 2000; c['regs'][0] = 2000 + da; c['regs'][1] = 2000 + rng.choice([0, 3]) ; c['regs'][3] = c['regs'][1] + db
                if rng.random() < 0.5: c['regs'][0] = 2000 - da; c['regs'][3] = c['regs'][1] - db
        if c['fn'] == 'f5969': c['rs'] = rng.randrange(1 << 32); c['nrec'] = rng.choice([1, 2, 3, 4]); c['sel'] = n % 3; c['own'] = 5 + rng.randrange(3)
        if c['fn'] == 'fdee4':
            c['qn'] = rng.choice([0, 1, 3, 5]); c['room'] = rng.choice([0x10, 0x1e, 0x3b, 0x3c, 0x3d, 0x50, 0x100]); c['len'] = rng.choice([0, 1, 5, 0x1e, 0x3c]); c['rb8'] = rng.randrange(256); c['dat'] = bytes(rng.randrange(256) for _ in range(0x40)).hex(); c['l0q'] = 0
        if c['fn'] in ('fe571', 'fe5ab', 'fe5f6'):
            c['wf'] = rng.choice([0, 1, 2, 3, 4, 5, 9, 1, 3]); c['w10'] = rng.choice([0, 0x18, 0x28, 0x58, 0x11, 0x20, 0x08]); c['w11'] = rng.choice([0x18, 0x28, 0x08, 0x38, 0x13, 0x21, 0x58, 0x18]); c['k19e7'] = rng.choice([0, 1, 1, 3]); c['l0w'] = rng.choice([0, 1, 2]); c['f3'] = 5 + c['f3'] % 3
        if c['fn'] == 'fe65b':
            c['cnt'] = rng.choice([1, 2, 3, 5, 6, 8, 8]); c['rel'] = [rng.choice([0, 1, 1, 7]) for _ in range(8)]; c['dv'] = [rng.choice([-1, 0, 1, 5000, 5000]) for _ in range(8)]
            c['qn'] = rng.choice([0, 1, 3, 4]); c['ids'] = [rng.choice([0x14, 0x15, 0x16, 0x17, 0x13]) for _ in range(4)]; c['qid'] = c['ids'][0]; c['l0q'] = 0
        if c['fn'] in ('fbea7', 'fbebb', 'fbed7', 'fbee6'):
            c['qn'] = rng.choice([0, 1, 3]); c['qid'] = 0x14; c['l0q'] = 0; c['ids'] = [0x14, 0x15, 0x16, 0x17]; c['f3'] = 5 + c['f3'] % 3; c['sel'] = rng.randrange(4)
            if c['fn'] in ('fbebb', 'fbee6'): c['regs'][0] = (c['regs'][0] & 0xffff0000) | (rng.randrange(256) << 8) | rng.randrange(0x40)
        if c['fn'] == 'fbf55': c['wf'] = rng.choice([9, 9, 9, 9, 5, 7, 0x19])
        if c['fn'] in ('fbf07', 'fbf55'): c['k31'] = rng.choice([0, 1, 2, 3]); c['dd'] = rng.choice([-1, 0, 1, 5, 100, -100])
        if c['fn'] in ('fdfe5', 'fe02a', 'fe03c', 'fe055', 'fe070'):
            c['qn'] = rng.choice([0, 1, 2, 3, 4, 6]); c['qe'] = [[rng.randrange(256), rng.randrange(1, 6), rng.randrange(4)] for _ in range(6)]
            if n % 3 == 0 and c['qn']: c['qe'][1 % c['qn']][1] = c['qe'][0][1]
            pick = c['qe'][rng.randrange(max(c['qn'], 1))]
            al, ah = (pick[1], pick[2]) if rng.random() < 0.7 else (rng.randrange(1, 7), rng.randrange(4))
            c['regs'][0] = (rng.randrange(1 << 16) << 16) | (ah << 8) | al; bl = rng.randrange(4); c['regs'][1] = (rng.randrange(1 << 16) << 16) | ((bl + rng.randrange(0, 4)) << 8) | bl
            c['l0q'] = al if n % 2 else rng.randrange(8); c['f3'] = 5 + c['f3'] % 3
        if c['fn'] == 'ff629':
            c['own'] = rng.randrange(4); c['k19e9'] = rng.choice([0, 0, 0x10, 1]); c['d3cda'] = rng.randrange(1 << 32); c['v1ca9'] = rng.randrange(1 << 32); c['f3'] = 8
            c['qn'] = rng.choice([0, 1, 2, 3]); c['qid'] = 0x14; c['l0q'] = 0
            c['ent'] = [[rng.choice([0xff, 0xff, 0xff, 0, 1, 2, 3]), rng.choice([-1, 0, 1, 5, 100]), rng.choice([0, 1, 1])] for _ in range(8)]
            c['ids'] = [rng.choice([0x14, 0x15, 0x16, 0x17, 0x13]) for _ in range(3)]
            c['txt'] = bytes(rng.randrange(0x20, 0x7d) for _ in range(0x100)).hex(); c['nv'] = [rng.choice([1, 2, 3]) for _ in range(4)]; c['eff'] = [rng.choice([0, 1, 2, 3]) for _ in range(4)]; c['rb'] = rng.randrange(256)
            c['calls'] = c['calls'] + [list(x) for x in c['calls']] + [list(x) for x in c['calls']]
            for call in c['calls']: call[5] = 0x8d4
        if c['fn'] == 'f11b4e': c['k4e'] = rng.choice([0, 1, 1, 1, 1]); c['calls'] = c['calls'] * 4; c['r1'] = rng.randrange(0, 30); c['r2'] = rng.randrange(0, 30)
        if c['fn'] in ('f6ba3', 'f6c04'):
            c['bk'] = rng.randrange(1 << 32); c['regs'][0] = rng.choice([0, 1, 2, 3, 0xb0, 0xb1, 0xb2, 0xb3]) | (rng.randrange(256) << 8); c['regs'][2] = rng.randrange(0, 560); c['regs'][3] = rng.randrange(0, 12)
            c['tbl'] = bytes(rng.randrange(256) for _ in range(256)).hex(); c['regs'][4] = 0x400900
        if c['fn'] in ('f6c66', 'f6cd6'):
            c['bk'] = rng.randrange(1 << 32); c['regs'][2] = rng.randrange(0, 500); c['regs'][3] = rng.randrange(0, 20); c['tbl'] = bytes(rng.randrange(256) for _ in range(256)).hex()
        if c['fn'] == 'f6d4a':
            cl_ = rng.randrange(1, 21); ch_ = rng.randrange(cl_, 24); c['regs'][2] = cl_ | (ch_ << 8); c['regs'][3] = rng.randrange(3, 31) | (rng.randrange(4, 15) << 8); c['regs'][4] = 0x400900 | rng.randrange(1, 256)
            c['calls'][0][1] = rng.randrange(0, 200); c['calls'][0][2] = rng.randrange(0, 20)
        if c['fn'] == 'f677f': c['regs'][4] = 0x400900
        if c['fn'] == 'f5ce': c['k4e'] = rng.choice([0, 1, 1, 1, 1, 1]); c['l0'] = [0, 1, 9, 0xa, 0x31, 0x32, 0x40, 0xff, 0x33, 0x30][n % 10]
        if c['fn'] in ('f7a1', 'f7d0', 'f862', 'f8c9'):
            for call in c['calls']: call[5] = 0x400900
            c['v7cf'] = rng.choice([-1, 0, 1, 5000]); c['seq'] = rng.choice([0, 1]); c['k19ea'] = rng.choice([0, 1, 2, 3]); c['b19e7'] = rng.choice([0, 1, 2, 3])
        if c['fn'] == 'f4473':
            c['cad'] = rng.choice([0xffffffff, 5, 0x12c, 0xffffffff]); c['k6c6'] = rng.choice([1, 2, 3, 4]); c['own'] = rng.randrange(4)
            c['lst'] = [rng.choice([0, 1, 2, 3, 0xff, 0xff]) for _ in range(4)] + [0xff]; c['lst'][rng.randrange(4)] = 0xff if n % 3 == 0 else rng.randrange(4); c['lst'] = c['lst'][:4] + [0xff]
            c['t'] = [rng.choice([0xff, 0xff, 0, 1]) for _ in range(4)]
        if c['fn'] == 'f44c0':
            c['k1ec6'] = rng.choice([0, 0, 0, 2]); c['p1974'] = rng.choice([0, 1, 1]); c['hp'] = rng.choice([0, 1, 5, 0x80, 0xff]); c['cad'] = rng.choice([0xffffffff, 5, 0xffffffff]); c['k9ea'] = rng.choice([0, 0, 1, 8, 0x80]); c['own'] = rng.randrange(4); c['b6ba'] = rng.randrange(1 << 32)
        if c['fn'] == 'f42bb':
            c['cnt'] = rng.choice([1, 2, 3]); c['k4e'] = rng.choice([0, 1, 1, 1]); c['k4f'] = rng.choice([1, 1, 2, 3]); c['k9ea'] = rng.randrange(256); c['k9ea'] = c['k9ea'] if n % 2 else (c['k9ea'] & 0xf8) - 1 & 255
            c['nch'] = rng.choice([1, 2, 3, 4]); c['st'] = rng.randrange(0, c['nch'] + 1)
        if c['fn'] == 'f59de': c['own'] = 5 + rng.randrange(3); c['b87'] = c['regs'][1] & 0xff; c['regs'][4] = 0x400800
        if c['fn'] in ('f6a73', 'f6acd'): c['m3'] = rng.choice([0, 1, 1]); c['qid'] = rng.choice([0x29, 0x31, 0x32, 0x2b, 0x2a]); c['qn'] = rng.choice([0, 1, 2, 3]); c['l0q'] = rng.choice([0, 0x29, 0x32])
        if c['fn'] == 'f5580': c['cu']['l2'] = rng.choice([0xa, 0xa, 0xb, 3, 0x9])
        if c['fn'] in ('f5593', 'f64ad', 'f5969', 'f59de', 'f5f2'): c['qid'] = rng.choice([0x30, 0x31, 0x2f, 0x30]); c['qn'] = rng.choice([0, 1, 2, 3]); c['l0q'] = rng.choice([0, 0x30, 0x31])
        if c['fn'] == 'f5f2':
            c['qn'] = rng.choice([0, 1, 2, 3, 4, 4, 5]); c['ids'] = [rng.choice([0x14, 0x15, 0x16, 0x17, 0x13]) for _ in range(5)]
            if n % 2: c['ids'] = [0x14, 0x15, 0x16, 0x17, 0x14][:5] if n % 4 == 1 else [0x17, 0x16, 0x15, 0x14, 0x13]
            c['qid'] = c['ids'][0]
        if c['fn'] == 'fd2d1': c['px'] = 160 + n * 7 % 340; c['py'] = 160 + n * 13 % 340
        if c['fn'] == 'fb319': c['px'] = n % 40; c['py'] = (n // 3) % 40
        if c['fn'] == 'fcb77': c['f22'] = [0x31, 0x31, 0x3b, 0, 0x40, 0x31][n % 6]; c['f19'] = rng.choice([1, 5, 0x80])
        if c['fn'] == 'fc0f6': c['f15'] = [2, 2, 3, 3, 3, 1, 0, 0xa][n % 8]; c['sel'] = (n // 8) % 4
    return out


def generate(game, names):
    constants = source_constants(game); report, code = analyze(game)
    o = ActionOracle(code, report['fs_assignments'], constants)
    o.cpu.mem_map(0xc00000, 0x80000)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def rel(v): return v - o.POOL if o.POOL <= v < o.POOL + 0x2000 else v
    def stub(addr, kind):
        def f():
            log.append(b'C' + struct.pack('<HIIIIII', addr & 0xffff, reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX), rel(reg(UC_X86_REG_EDI)), rel(reg(UC_X86_REG_ESI))))
            if addr == 0x6885: o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + TMPL); o.ret(); return   # the template lookup keeps everything but EDI
            if addr == 0x68bd: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TMPL); o.ret(); return   # the template lookup keeps everything but EDX
            owner_in = reg(UC_X86_REG_EAX) & 255
            zf, ax, bx, cx, dx, di = state['calls'].pop(0) if state['calls'] else (1, 0, 0, 0, 0, 0)
            if addr in (0x9e41, 0x9e55, 0x9e69):   # the random byte: only EAX (and ZF) change
                o.cpu.reg_write(UC_X86_REG_EAX, ax); o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret(); return
            o.cpu.reg_write(UC_X86_REG_EAX, ax); o.cpu.reg_write(UC_X86_REG_EBX, bx); o.cpu.reg_write(UC_X86_REG_ECX, cx)
            o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TMPL if addr in (0xc4cd, 0x982b) else dx)
            if addr == 0x1ead8: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0xa000 + 0x1a35 * owner_in)
            if addr != 0x43b0: o.cpu.reg_write(UC_X86_REG_EDI, di)   # 43B0 keeps EDI (the animal action relies on it)
            if addr == 0xa758 or addr == 0xac63 or (addr == 0xc4cd and state['fn'] in ('fcb77', 'fcc7b')): o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + (OBJ1 if di & 1 else OBJ2))
            if addr == 0xd1ba and state['fn'] == 'fcb77': o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + OBJ2)
            if addr == 0xbadf: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0x1900 + 4 * ((ax >> 8) & 0xff))
            if addr == 0xbb26: o.cpu.reg_write(UC_X86_REG_ECX, o.POOL + (OBJ1 if cx & 1 else OBJ2))
            o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret()
        return f
    digest = hashlib.sha256(); all_cases = cases(names)
    for case in all_cases:
        if os.environ.get('ONLY_CASE') and str(all_cases.index(case)) != os.environ['ONLY_CASE']: continue
        log.clear(); state['calls'] = [list(c) for c in case['calls']]; state['fn'] = case['fn']
        entry, end, callees = FUNCS[case['fn']]
        o.prepare(32, 32); o.mode = 'actions'
        o.RANGES['actions'] = ([(entry, end)], [])
        o.stubs = {a: stub(a, None) for a in callees if a not in (0x6885, 0x68bd)}
        o.RANGES['actions'] = ([(entry, end), (0x6877, 0x68d8), (0x4409, 0x4442), (0xdb47, 0xdc91), (0xa416, 0xa444), (0x6361, 0x6377), (0x6377, 0x63cb), (0x6423, 0x643c), (0x12ae1, 0x12e12), (0xc331, 0xc35f), (0xd12c, 0xd182), (0x67bd, 0x6877), (0xa320, 0xa38a), (0x4580, 0x4870), (0x67c0, 0x6877), (0x4965, 0x4b03), (0xa4a9, 0xa4c2), (0x4b03, 0x5716), (0x5af6, 0x5c2e), (0x5cc2, 0x6361), (0xf5f2, 0xf629), (0x6ba3, 0x6c66), (0x63cb, 0x6423), (0x643c, 0x64ad), (0xf781, 0xf791), (0xe055, 0xe070), (0xe571, 0xe5f6), (0xf791, 0xf7a1), (0xe070, 0xe08b), (0xdfe5, 0xe02a), (0x6996, 0x69fe), (0x7012, 0x704f), (0x66df, 0x66e9), (0xdf2a, 0xdfe5)], [])
        o.cpu.mem_write(o.POOL, bytes(0x3000)); o.cpu.mem_write(o.FS + 0x17f, bytes(4)); o.cpu.mem_write(0x4c0, bytes(0x110)); o.cpu.mem_write(0, bytes(0x40)); o.cpu.mem_write(0xf0b0, bytes(0x10)); o.cpu.mem_write(0x40, bytes(0x20)); o.cpu.mem_write(0x27f0, bytes(0x60)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x1922, bytes(12)); o.cpu.mem_write(0x1a10, bytes(0x10)); o.cpu.mem_write(0x43c, bytes(0x204)); o.cpu.mem_write(0xed20, bytes(4)); o.cpu.mem_write(0xc00000, b'\x5a' * 0x8000); o.cpu.mem_write(0x3cd0, bytes(0x20)); o.cpu.mem_write(0xefb, bytes(0x80)); o.cpu.mem_write(0x1ce1, bytes(0x40)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        r = bytearray(prng(case['r'], 0x50)); r[0] %= 3; r[0xe] = 0; r[3] = case['f3']; struct.pack_into('<II', r, 4, case['px'], case['py']); struct.pack_into('<I', r, 0x28, case['cur']); r[0x14] = case['f14']; r[0x19] = case['f19']; r[0x15] = case['f15']; r[0x31] = case['f31']; r[0x30] = case['f30']
        if 'f22' in case: r[0x22] = case['f22']
        if 'f23' in case: r[0x23] = case['f23']; struct.pack_into('<I', r, 0x24, case['f24'])
        struct.pack_into('<H', r, 0x17, case['f17']); struct.pack_into('<I', r, 0x1c, case['f1c'])
        o.cpu.mem_write(o.POOL + POOLREC, bytes(r)); ob1, ob2 = bytearray(constrain(prng(case['o1'], 0x50))), bytearray(constrain(prng(case['o2'], 0x50)))
        if case['fn'] == 'fcb77': ob1[3] %= 3; ob2[3] %= 3
        if case['fn'] == 'fs14b' and case['sel'] != 0:
            for ob in (ob1, ob2): ob[0x20] = 0xff; ob[0x19] = 0x7f; ob[3] %= 8
        o.cpu.mem_write(o.POOL + OBJ1, bytes(ob1)); o.cpu.mem_write(o.POOL + OBJ2, bytes(ob2)); o.cpu.mem_write(o.POOL + TMPL, prng(case['tm'], 0x100))
        for k in range(4):
            pl = prng(case['map'] + k, 1024); o.cpu.mem_write(o.POOL + 0x4000 + 0x1000 * k, b''.join(struct.pack('<I', (0, OBJ1, OBJ2)[b % 3]) for b in pl)); o.cpu.mem_write(0x18ce + 4 * k, struct.pack('<I', o.POOL + 0x4000 + 0x1000 * k))
        if case['fn'] in ('fd182', 'fd19c'): o.cpu.mem_write(o.POOL + TMPL + 0x3b, struct.pack('<I', o.CODE + 0x30))
        for ar in range(3):
            tb = bytearray(0x56); struct.pack_into('<I', tb, 4, o.POOL + TMPL); o.cpu.mem_write(0x6ca + 0x56 * ar, bytes(tb))
        n = max(case['nch'], 1) if case['fn'] == 'fb6f1' else case['nch']; st = min(case['st'], n)
        for k in range(n):
            ob = bytearray(constrain(prng(case['o1'] + 7 + k, 0x50))); struct.pack_into('<I', ob, 0x10, 0x60)
            if ob[0x1c] & 1: ob[0x1b] = case['regs'][1] & 0xff
            if case['fn'] == 'f59de': ob[0xe] = 0
            if case['fn'] == 'f42bb':
                ob[0xe] = 0; struct.pack_into('<I', ob, 0x1c, (0x8000 + 0x60 * (ob[0x1d] % max(n, 1))) if ob[0x1e] & 1 else 0)
            if case['fn'] == 'fb319': ob[0x19] = 0x7f; struct.pack_into('<II', ob, 4, 900 + ob[0x1d] % 120, 850 + ob[0x1e] % 100)
            if case['fn'] == 'fb5d8' and ob[0x1c] & 2: ob[0x1b] = 1 + ob[0x1d] % (n + 1)
            o.cpu.mem_write(o.POOL + 0x8000 + 0x60 * k, bytes(ob))
        for ow in range(8):
            pb = bytearray(prng(case['pt'] + ow, 0xf5)); struct.pack_into('<I', pb, 9, 0x8000 + 0x60 * n); struct.pack_into('<I', pb, 0xe, 0x8000 + 0x60 * st); o.cpu.mem_write(0x97a + 0xf5 * ow, bytes(pb))
        lst = [o.POOL + OBJ1 if (case['pt'] >> k) & 1 else o.POOL + OBJ2 for k in range(case['pt'] % 7)] + [0]
        o.cpu.mem_write(0x1f45, struct.pack('<%dI' % len(lst), *lst)); o.cpu.mem_write(0x1ca2, bytes(0x20))
        if case['fn'] == 'fd77a': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        tab = bytearray(); tv = prng(case['tab'], 256)
        for i in range(256): tab += bytes([tv[i] & 0x7f, (i * 167 + (case['tab'] & 0xff)) & 0xff])
        o.cpu.mem_write(o.STACK + 0x900, bytes(tab) + b'\xff\xff'); o.cpu.mem_write(o.POOL + 0x1900, struct.pack('<4I', o.POOL + OBJ1, o.POOL + OBJ2, o.POOL + OBJ1, o.POOL + OBJ2))
        o.cpu.mem_write(0x1ccb, bytes(0x16 * 256 + 4)); o.cpu.mem_write(0x1f45, struct.pack('<%dI' % len(lst), *lst))
        dsb = prng(case['ds'], 0x2bc)
        for k, (wa, wn) in enumerate(DS_WINDOWS): o.cpu.mem_write(wa, dsb[sum(n for _, n in DS_WINDOWS[:k]):sum(n for _, n in DS_WINDOWS[:k + 1])])
        o.cpu.mem_write(o.FS + 0x13d, bytes([case['fs13d']])); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        o.cpu.mem_write(0x273f, bytes([case['g273'], case['g274']]))
        o.cpu.mem_write(0x525, bytes([case['g525'], case['g526']])); o.cpu.mem_write(0x1c61, bytes([case['g1c61']])); o.cpu.mem_write(0x2730, struct.pack('<I', case['g2730'])); o.cpu.mem_write(0x6b6, struct.pack('<I', o.POOL + 0x1000)); o.cpu.mem_write(o.POOL + 0x1000, prng(case['rt'], 0x800))
        o.cpu.mem_write(o.POOL + 0xa000, prng(case['map'] + 9, 8 * 0x1a35)); o.cpu.mem_write(0x6ba, struct.pack('<I', o.POOL + 0xa000))
        o.cpu.mem_write(0x18e6, struct.pack('<II', 1024, 1024))
        o.terrain(32, 32, [b % 8 for b in prng(case['map'], 1024)])
        o.cpu.mem_write(0x1c9f, bytes([case['g1c9f']])); o.cpu.mem_write(0x512, struct.pack('<I', case['g512'])); o.cpu.mem_write(0x51a, struct.pack('<I', case['g51a'])); o.cpu.mem_write(0x51e, struct.pack('<I', case['g51e'])); o.cpu.mem_write(0x522, bytes([case['g522']]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + POOLREC); o.cpu.reg_write(UC_X86_REG_EBP, o.POOL + TMPL)
        o.RET = None; o.cpu.mem_write(0x50e, bytes(4))
        if case['fn'] == 'f8227':
            table = o.STACK + 0x7e8; o.RET = table
            o.cpu.mem_write(table, b''.join(struct.pack('<i', (o.CODE + a) - table) for a in callees)); o.cpu.mem_write(0x50e, bytes(4))
        for q, v in zip((UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI), case['regs']): o.cpu.reg_write(q, v)
        if case['fn'] == 'fd77a': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] == 'fbb8b': o.cpu.reg_write(UC_X86_REG_EDX, 0x100)
        if case['fn'] == 'f68a0': o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffff00ff) | ((((case['regs'][0] >> 8) & 0xff) % 3) << 8))
        if case['fn'] == 'fd19c': o.cpu.reg_write(UC_X86_REG_EBX, ((((case['regs'][1] >> 8) & 0xff) % 3) << 8))
        if case['fn'] == 'fc35f': o.cpu.reg_write(UC_X86_REG_EDX, case['regs'][3] & 3)
        if case['fn'] == 'fc344': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] in ('fc0f6', 'fbe2e'):
            if case['fn'] == 'fc0f6': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + TMPL)
            o.cpu.reg_write(UC_X86_REG_EAX, case['regs'][0] & 0xffffff00)
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); pa = 0x97a + 0xf5 * case['f3']; pb = bytearray(o.cpu.mem_read(pa, 0xf5))
            for k in (0x7c, 0x80, 0x84, 0x88):
                if case['sel'] & 1: struct.pack_into('<I', tb, k, struct.unpack_from('<I', tb, k)[0] & 0xff)
            for k in (0x18, 0x1c, 0x20, 0x24):
                if case['sel'] & 2: struct.pack_into('<I', pb, k, struct.unpack_from('<I', pb, k)[0] | 0x10000)
            o.cpu.mem_write(o.POOL + TMPL, bytes(tb)); o.cpu.mem_write(pa, bytes(pb))
        if case['fn'] == 'fb616':
            r2, r3 = case['regs'][2], case['regs'][3]; b1 = prng(case['o1'], 0x50)[3]; b2 = prng(case['o2'], 0x50)[3]
            o.cpu.reg_write(UC_X86_REG_ECX, (r2 & 0xffff0000) | (r2 & 31) | (((r2 >> 8) & 31) << 8)); o.cpu.reg_write(UC_X86_REG_EDX, (r3 & 0xffff0000) | (r3 & 31) | (((r3 >> 8) & 31) << 8))
            o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffffff00) | (b1 if case['sel'] & 1 else b2 if case['sel'] & 2 else case['regs'][0] & 0xff))
        if case['fn'] in ('fb9eb', 'fba2e', 'fb5d8', 'fda45'): o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffffff00) | (case['regs'][0] & 7))
        if case['fn'] == 'fb319' and case['sel'] != 0:
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x46] = 5; tb[0x4e] = 0x10; tb[0x40] |= 8; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] == 'fb6f1':
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x8000); o.cpu.reg_write(UC_X86_REG_EBP, o.POOL + 0x8000 + 0x60 * n)
            tb = prng(case['tm'], 0x100)
            for k in range(6):
                if (case['sel'] >> (k % 2)) & 1: o.cpu.mem_write(0x1978 + 0xc * k + 7, bytes([tb[0x12 + 8 * k]]))
            o.cpu.mem_write(0x1c87, bytes([case['regs'][1] & 0xff]))
        if case['fn'] == 'f6ec1':
            o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + OBJ1)); tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100))
            for k in range(6): struct.pack_into('<I', tb, 0x0d + 8 * k, struct.unpack_from('<I', tb, 0x0d + 8 * k)[0] % 0x30)
            o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] == 'fd2d1':
            sel, r1, r2, r3 = case['sel'], case['regs'][1], case['regs'][2], case['regs'][3]
            o.cpu.reg_write(UC_X86_REG_EAX, (0x900, 0xa00, 0, 0x900)[sel]); o.cpu.reg_write(UC_X86_REG_EBX, (0xa00, 0x900, r3, 0)[(sel + r2) & 3])
        if case['fn'] in ('f7012', 'f704f', 'f7069'):
            txt = bytes.fromhex(case['txt']); font = bytearray(bytes.fromhex(case['font']))
            for i in range(256): struct.pack_into('<H', font, 2 * i, 0x200 + (i % 8) * 4)
            o.cpu.mem_write(o.POOL + 0x2200, txt); o.cpu.mem_write(o.POOL + 0x2400, bytes(font)); o.cpu.mem_write(0x19ee, struct.pack('<I', o.POOL + 0x2400))
            o.cpu.mem_write(o.POOL + 0x2800, bytes(0x100)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x2200)
            for ow in range(8):
                pa = 0x97a + 0xf5 * ow; pbb = bytearray(o.cpu.mem_read(pa, 0xf5)); pbb[0xa4 + (ow * 3 + case['nch']) % 30] = 0xff; o.cpu.mem_write(pa, bytes(pbb))
        if case['fn'] in ('f9bf9', 'f9c57'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v).to_bytes(n, 'little'))
            wd(0x1c35, case['w35'], 2); wd(0x1cad, case['d1cad'], 4); wd(0x1c4e, case['b1c4e'], 1); wd(0x1ca9, case['d1ca9'], 4); wd(0x19e9, case['b19e9'], 1); wd(0x9090, case['b9090'], 1); wd(0x9091, case['b9091'], 1)
            lst = bytes.fromhex(case['lst']); o.cpu.mem_write(0x1cb4, b'\x00' + lst); wd(0x1972, case['b1972'], 1); wd(0x1cb1, case['d1cb1'], 4)
            o.cpu.mem_write(0x1ccb, bytes(b if b != 0xff else 0xfe for b in prng(case['ds'] + 40, 0x16 * 9)) if case['ff'] % 4 else bytes([0xff] * (0x16 * 4)) + bytes(0x16 * 5)); o.cpu.mem_write(o.FS + 0x17f, bytes(4))
        if case['fn'] == 'fa38a':
            o.cpu.mem_write(o.POOL + 0x2000, struct.pack('<I', (0, 0x900, 0xa00)[case['sel']]))
        if case['fn'] == 'fa47e':
            rr = bytearray(o.cpu.mem_read(o.POOL + POOLREC, 0x50)); rr[1] %= 3; o.cpu.mem_write(o.POOL + POOLREC, bytes(rr))
        if case['fn'] in ('f6377', 'f6361', 'f63cb', 'f63f7', 'f640b', 'f6423', 'f643c', 'f645b', 'f66bb', 'f66df', 'f67bd', 'f67c0', 'f67d3', 'f616f', 'f6267', 'f6294', 'f64ad', 'f6562', 'f658a', 'f65df'):
            fn, sel = case['fn'], case['sel']; wi = bytearray(bytes.fromhex(case['wi'])); ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50))
            if fn != 'f645b': wi[3] %= 8
            o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + OBJ1)); o.cpu.mem_write(0x1cad, struct.pack('<I', case['cad']))
            if fn in ('f6377', 'f6361', 'f6294'):
                wi[2] = 0; wi[0x14] %= 3; ob[3] %= 8
                if fn == 'f6361': wi[0x1a] = 1 if sel & 1 else wi[0x1a]
                if fn == 'f6294': wi[0x1a] = wi[6] & 1; ob[0x40] = 0xff; ob[0x26] = 0xff if wi[5] & 1 else ob[0x26]
                tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100))
                if sel & 2: tb[0x40] |= 8
                else: tb[0x40] &= 0xf7
                if sel & 1:
                    for k in range(0x7c, 0x8c): tb[k] = tb[k] % 4 if k % 4 == 0 else 0
                o.cpu.mem_write(o.POOL + TMPL, bytes(tb)); pa = 0x97a + 0xf5 * ob[3]; pbb = bytearray(o.cpu.mem_read(pa, 0xf5))
                if sel & 1:
                    for k in range(0x18, 0x28): pbb[k] = 100 if k % 4 == 0 else 0
                o.cpu.mem_write(pa, bytes(pbb))
            if fn == 'f616f':
                wi[2] %= 6; wi[0x1a] = wi[0x1a] if sel == 3 else 0; sl = 0x1978 + 12 * wi[2]
                o.cpu.mem_write(sl + 6, bytes([[0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0xb, 0xa][wi[5] % 14]]))
                if sel & 1: o.cpu.mem_write(sl + 9, bytes(o.cpu.mem_read(0x1c35, 2)))
                o.cpu.mem_write(0x1c89, bytes([wi[9] & 1]))
            if fn == 'f64ad': wi[3] = 5 + wi[3] % 3
            if fn in ('f64ad', 'f6562', 'f658a'):
                for k in range(0x22, 0x27): ob[k] = 0xff if (wi[k] & 1) else 0
                ob[0] = sel % 3; ob[0xf] = 1 if wi[5] & 1 else ob[0xf]
                if fn == 'f658a': wi[0x1a] = (0xd + sum(ob[0x22:0x27])) & 0xff if wi[6] & 1 else wi[0x1a]
                if fn == 'f64ad': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + POOLREC); wi[0x1a] = wi[0x1a]
            if fn == 'f65df':
                if case['nch'] == 0: o.cpu.mem_write(o.POOL + 0x8000, bytes(0x60 * 8))
                k = wi[0x18] % max(case['nch'], 1); struct.pack_into('<I', wi, 0x18, 0x8000 + 0x60 * k)
                if wi[5] & 1: wi[2] = o.cpu.mem_read(o.POOL + 0x8000 + 0x60 * k, 0x20)[0x1b]
                if wi[6] & 1: wi[0x1d] = o.cpu.mem_read(0x1972, 1)[0]
                o.cpu.mem_write(2, bytes([wi[7] % 3 // 2]))
                o.cpu.mem_write(0x1c96, bytes([wi[9]]))
            if fn == 'f6267':
                l2 = [0, 2, 7, 0xa, 0x10][wi[7] % 5]
                if wi[8] & 2 and not wi[8] & 1: l2 = 0xa
                o.cpu.mem_write(2, bytes([l2]))
                if wi[8] & 1: wi[0x1b] = l2
                elif wi[8] & 2: wi[0x1b] = 7
            if fn == 'f63cb' and sel & 1: wi[0x14] = ob[0x19]
            if fn == 'f63f7' and sel & 1: wi[0x14] = ob[0x16]
            if fn == 'f640b': wi[0x1a] %= 0x40; ob_idx = wi[0x1a]; wi[0x14] = ob[ob_idx] if sel & 1 else wi[0x14]
            if fn == 'f6423' and sel & 1: wi[0x14] = ob[0x27]
            if fn == 'f643c':
                struct.pack_into('<I', wi, 0x15, o.POOL + OBJ1); wi[0x14] = 1 if sel & 1 else wi[0x14]
                if sel & 2: struct.pack_into('<I', wi, 0x1a, (struct.unpack_from('<I', ob, 0)[0] & (0xff if wi[0x14] == 1 else 0xffffff)))
            if fn == 'f645b':
                wi[0x1a] %= 0x40; wi[3] = [0, 1, 3, 3][sel]; wi[0x12] = 0x7f + wi[0x12] % 0x7e; wi[0x15] %= 130
            if fn in ('f67bd', 'f67c0', 'f67d3'):
                o.cpu.mem_write(0x1906, struct.pack('<II', case['regs'][2] % 12, case['regs'][3] % 12)); o.cpu.mem_write(0x190a, struct.pack('<I', case['regs'][3] % 12)); o.cpu.mem_write(0x1906, struct.pack('<I', case['regs'][2] % 12))
                o.cpu.mem_write(0x18da, struct.pack('<I', o.POOL + 0x2e00)); o.cpu.mem_write(o.POOL + 0x2e00, prng(case['ds'] + 50, 1024))
                seg = bytearray(prng(case['map'] + 3, 8192)); o.cpu.mem_write(o.SEG, bytes(seg))
            r[3] = wi[3]; (o.cpu.mem_write(0x97a + 0xf5 * wi[3], bytes(0xf5)) if wi[3] >= 8 else None); o.cpu.mem_write(o.POOL + OBJ1, bytes(ob)); o.cpu.mem_write(o.POOL + POOLREC, bytes(wi) + bytes(o.cpu.mem_read(o.POOL + POOLREC + 0x30, 0x50 - 0x30)))
        if case['fn'] in ('f12ae1', 'f12b29', 'f12ba3', 'f12bd8', 'f12bf2', 'f12bfb', 'f12c10', 'f12c48', 'f12c65', 'f12cb1', 'f12d08'):
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); sel = case['sel']
            if sel & 1: tb[0x72] = 0xff; tb[0x73] = tb[0x73] % 4; tb[0x74] = 0xf
            tb[0x4f] = 1 + tb[0x4f] % 6; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
            o.cpu.mem_write(0x522, bytes([[3, 6, 1, case['g522']][(sel >> 1) & 3]])); o.cpu.mem_write(0x4790, bytes(b & 255 for b in HEADING))
            o.cpu.mem_write(0x512, struct.pack('<I', case['g512'] if sel & 2 else 0))
        if case['fn'] in ('faab3', 'fac23'):
            if case['fn'] == 'faab3':
                owner = case['sel'] % 8; o.cpu.mem_write(0x1972, bytes([owner])); pbb = bytes(o.cpu.mem_read(0x97a + 0xf5 * owner, 0xf5))
                for k, (a, shown) in enumerate(((0x18, 0x1c62), (0x1c, 0x1c66), (0x20, 0x1c6a))): o.cpu.mem_write(shown, struct.pack('<I', (struct.unpack_from('<I', pbb, a)[0] + case['deltas'][k]) & 0xffffffff))
            o.cpu.mem_write(0x19fe, struct.pack('<9I', 100000000, 10000000, 1000000, 100000, 10000, 1000, 100, 10, 1))
        if case['fn'] in ('f4580', 'f4672', 'f46f7', 'f478d', 'f486d', 'f4938', 'f4965', 'f4b03', 'f4b12', 'f4c46', 'f4d41', 'f4d50', 'f4e27', 'f4e40', 'f4fce', 'f500d', 'f50e5', 'f50f4', 'f520b', 'f5297', 'f52a6', 'f538d', 'f53a8', 'f53c4', 'f53d3', 'f5679', 'f5573', 'f5580', 'f5593', 'f55b5', 'f55d1', 'f55e0', 'f566a'):
            cu = case['cu']; owner = cu['own']
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            wd(0x1c60, cu['c60'], 1); wd(0x193a, cu['b193a'], 1); wd(0x194b, cu['b194b'], 1); wd(0x1c58, cu['vx'], 4); wd(0x1c5c, cu['vy'], 4); wd(0x194d, cu['vx'] + cu['mx'] % 20, 4); wd(0x1951, cu['vy'] + cu['my'] % 20, 4)
            wd(0x1972, owner, 1); wd(0x1971, cu['b1971'], 1); wd(0x1c89, cu['g89'], 1); wd(0x1c87, cu['b87'], 1); wd(0x1986, cu['b86'], 1); wd(0x198a, cu['b8a'], 1); wd(0x19c0, cu['o19c0'], 1); wd(0xed20, cu['ed20'], 4)
            o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + (OBJ1 if case['sel'] & 1 else OBJ2)))
            o.cpu.mem_write(0x0, bytes(0x40)); wd(0x0, cu['l0'], 1); wd(0x12, cu['l12'], 1); wd(0x13, cu['l13'], 1); wd(0x5, o.POOL + (OBJ1 if cu['l5'] == 1 else OBJ2) if cu['l5'] else 0, 4); wd(0x9, o.POOL + TMPL, 4); wd(0xd, cu['ld'], 1)
            o.cpu.mem_write(0x1906, struct.pack('<II', 0, 0)); o.cpu.mem_write(0x18da, struct.pack('<I', o.POOL + 0x2e00)); o.cpu.mem_write(o.POOL + 0x2e00, prng(case['ds'] + 50, 1024))
            o.cpu.mem_write(o.SEG, bytes(prng(case['map'] + 3, 8192)))
            if case['fn'] in ('f4b03', 'f4b12', 'f4c46', 'f4d41', 'f4d50', 'f4e27', 'f4e40', 'f4fce', 'f500d', 'f50e5', 'f50f4', 'f520b', 'f5297', 'f52a6', 'f538d', 'f53a8', 'f53c4', 'f53d3', 'f5679', 'f5573', 'f5580', 'f5593', 'f55b5', 'f55d1', 'f55e0', 'f566a'):
                for a, k in ((0x199e, 'v99e'), (0x19a2, 'v9a2'), (0x19aa, 'v9aa'), (0x19ae, 'v9ae'), (0x1992, 'v92'), (0x1996, 'v96'), (0x4c, 'c4c')): wd(a, cu[k], 1)
                wd(0x27f4, cu['v27f4'], 1); wd(0x4190, cu['b87'], 1); wd(0x4f0, cu['a4f0'], 4); wd(0x4f4, cu['a4f4'], 4); wd(0x4f8, cu['a4f8'], 4); wd(0x4fc, cu['a4fc'], 4); wd(0x2849, o.POOL + TMPL, 4)
                for base in (OBJ1, OBJ2): o.cpu.mem_write(o.POOL + base + 0x22, bytes([0]))
                wd(0x1c35, cu['k35'], 2); wd(0xe, 0 if cu['e0'] else o.POOL + OBJ1, 4); wd(0x1955, cu['d55'] * 32, 4); wd(0x1959, cu['e0'] * 64, 4); wd(0x1c7e, 96, 4); wd(0x1c82, 32 * cu['d55'], 4); wd(0xf0b7, 0, 4); wd(0x4, cu['bx'], 1)
                wd(0x5, 0 if cu['l5b'] == 0 else o.POOL + (OBJ1 if cu['l5b'] == 1 else OBJ2), 4); wd(0x9, o.POOL + TMPL, 4); wd(0xd, cu['ld'] | (0x10 if cu['e0'] else 0), 1); wd(0x2, 0, 1); wd(0x12, 0, 1)
                tbb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100))
                if case['sel'] & 1: tbb[0x41] = (tbb[0x41] & 0xf0) | 4
                o.cpu.mem_write(o.POOL + TMPL, bytes(tbb))
            if case['fn'] == 'f4965':
                wd(0x1c86, cu['c86'], 1); wd(0x1c35, cu['k35'], 2); wd(0x1974, 0 if cu['p74'] == 0 else o.POOL + (OBJ1 if cu['p74'] == 1 else OBJ2), 4); wd(0x3, cu['l3'], 1); wd(0x4, cu['l13'], 1); wd(0x0, cu['l0b'], 1)
                wd(0x1955, cu['d55'], 4); wd(0x1959, cu['d55'] ^ cu['e0'], 4); wd(0x1c7e, cu['d55'] if cu['e0'] else 7, 4); wd(0x1c82, cu['d55'] ^ cu['e0'], 4); wd(0xf0b7, 0, 4)
            if case['fn'] in ('f486d', 'f4938'):
                for base in (OBJ1, OBJ2): o.cpu.mem_write(o.POOL + base + 0x19, bytes([cu['hp']]))
                wd(0x194d, 0x10 + cu['px'], 4); wd(0x1951, 0x10 + cu['py'], 4); wd(0x2, cu['l2'], 1); wd(0x0, cu['l0b'], 1); wd(0x4, cu['l13'], 1)
                for k in range(8): wd(0x14 + 4 * k, o.CODE + 0x20232, 4)
        if case['fn'] in ('fdec8', 'fdf2a', 'fdf7e'):
            qb = bytearray(prng(case['ds'] + 30, 0x604))
            for e in range(0x7f2, 0x7f2 + 0x1e * 40, 0x1e): qb[e - 0x7d4 + 1] = qb[e - 0x7d4 + 1] % 4
            o.cpu.mem_write(0x7d4, bytes(qb)); o.cpu.mem_write(0xdce, struct.pack('<I', 0x7f2 + 0x1e * ((case['nch'] * 3 + case['st']) % 45 if case['fn'] == 'fdf7e' else case['nch'] % 5)))
            o.cpu.mem_write(0x0, bytes([prng(case['ds'] + 32, 1)[0] % 4, 0])); o.cpu.mem_write(o.POOL + 0x2200, prng(case['ds'] + 31, 0x1e)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x2200)
            o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffffff00) | (case['regs'][0] % 4))
        if case['fn'] in ('f5960', 'f5af6', 'f5b55'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            wi = bytearray(bytes.fromhex(case['wi'])); ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50)); sel = case['sel']
            wi[3] = case['own']; r[3] = wi[3]; ob[0x19] = case['hp']
            if sel & 1: wi[0x1a] = ob[0xe]
            else: wi[0x1a] = (ob[0xe] + 1) & 255
            wi[0x1d] = case['k1d']; wi[0x1c] = case['k1c']; wi[0x12] = case['t12']; wi[0x1b] = 0x3f if sel == 2 else (wi[0x1b] & 0xc0) | (sel * 5)
            wd(0x1969, o.POOL + OBJ1, 4); wd(0x1972, sel * 2 % 8, 1); wd(0x1c89, (sel >> 1) & 1, 1); wd(0x19e7, case['b19e7'], 1)
            wd(0x18da, o.POOL + 0x2e00, 4); o.cpu.mem_write(o.POOL + 0x2e00, prng(case['ds'] + 50, 1024))
            txt = bytes.fromhex(case['txt']); font = bytearray(bytes.fromhex(case['font']))
            for i in range(256): struct.pack_into('<H', font, 2 * i, 0x200 + (i % 8) * 4)
            o.cpu.mem_write(o.POOL + 0x2200, txt); o.cpu.mem_write(o.POOL + 0x2400, bytes(font)); wd(0x19ee, o.POOL + 0x2400, 4)
            for k in range(6): wd(0x1978 + 0xc * k + 2, o.POOL + 0x2200, 4)
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['qid'] if k == 0 else (case['qid'] + 1)])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, case['l0q'], 2)
            o.cpu.mem_write(o.POOL + OBJ1, bytes(ob)); o.cpu.mem_write(o.POOL + POOLREC, bytes(wi) + bytes(o.cpu.mem_read(o.POOL + POOLREC + 0x30, 0x20)))
        if case['fn'] in ('f5e1c', 'f5f0a', 'f5fc5', 'f6057', 'f608e', 'f612b', 'f6142', 'f7a1', 'f7d0', 'f862', 'f8c9', 'fe5e6', 'fe621', 'fe636', 'fbf07', 'fbf55'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            wi = bytearray(prng(case['ds'] + 51, 0x80)); wi[3] = case['wf']; wi[3] = wi[3] + 8 if case['fn'] == 'f7d0' and wi[3] < 8 else wi[3]; r[3] = wi[3]; struct.pack_into('<H', wi, 0x1a, case['w1a']); wi[0x1c] = case['k1c']; wi[0x1d] = case['k1d']
            t22 = case['t22']; struct.pack_into('<I', wi, 0x16, t22 if case['m16'] else case['r16'])
            if case['fn'] == 'f7d0': wi[0x1d] %= 8
            if case['fn'] == 'fbf07': wi[0x31] = case['k31']
            if case['fn'] == 'fbf55':
                ea = (((wi[0x14] | wi[0x15] << 8) >> 1) + struct.unpack_from('<I', wi, 8)[0]) & 0xffffffff; case['d1951'] = (ea + case['dd']) & 0xffffffff
            if case['fn'] == 'f6057' and case['m16']: struct.pack_into('<I', wi, 0x1a, t22)
            if case['fn'] == 'f608e': wi[0x1b] = case['k1d']; struct.pack_into('<I', wi, 4, case['pos'])
            wd(0x19c2, case['b19e7'], 1); wd(0x1969, o.POOL + OBJ1, 4)
            wd(0x1972, case['own'], 1)
            if case['fn'] == 'f5f0a': tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x45] = 64 + case['k1d'] % 190; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
            ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50)); struct.pack_into('<I', ob, 0x22, t22); o.cpu.mem_write(o.POOL + OBJ1, bytes(ob))
            wd(0x1c35, case['k35'], 2); wd(0x19e7, case['b19e7'], 1); wd(0x1951, case['d1951'], 4); wd(0xed20, 0x400900 - 0x9df, 4); wd(0x1c87, case['b87'], 1)
            o.cpu.mem_write(o.POOL + POOLREC, bytes(wi)); o.cpu.mem_write(o.POOL + 0x2800, bytes(0x100))
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['qid'] if k == 0 else (case['qid'] + 1)])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, case['l0q'], 2)
        if case['fn'] in ('f66e9', 'f670f', 'f672f', 'f674f'): o.cpu.mem_write(0x1922, struct.pack('<I', case['u22'])); o.cpu.mem_write(0x192a, struct.pack('<I', case['u2a']))
        if case['fn'] == 'f5580': o.cpu.mem_write(2, bytes([case['cu']['l2']]))
        if case['fn'] in ('f6996', 'f69fe'):
            o.cpu.mem_write(0x287e, prng(case['tab'], 256)); ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50)); ob[3] = case['s3']; ob[4] = case['s4']; o.cpu.mem_write(o.POOL + OBJ1, bytes(ob))
        if case['fn'] in ('f6a73', 'f6acd'):
            o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + OBJ1)); o.cpu.mem_write(0x1972, bytes([o.cpu.mem_read(o.POOL + OBJ1 + 3, 1)[0] if case['m3'] else (o.cpu.mem_read(o.POOL + OBJ1 + 3, 1)[0] + 1) & 255]))
        if case['fn'] == 'f5969':
            buf = bytearray(prng(case['rs'], 0x200)); pos = 0; rs = case['rs']
            for k in range(case['nrec']):
                buf[pos] = 0x80 if (rs >> k) & 1 else 0x20; buf[pos + 1] = 1; buf[pos + 3:pos + 7] = (0 if (rs >> (k + 8)) & 1 else k + 2).to_bytes(4, 'little'); cnt = buf[pos + 0xf] % 3; buf[pos + 0xf] = cnt; pos += 0x10 + 2 * cnt
            buf[pos + 1] = 0xff; o.cpu.mem_write(o.POOL + 0x2200, bytes(buf))
            ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50)); ob[0] = case['sel']; ob[3] = case['own']; o.cpu.mem_write(o.POOL + OBJ1, bytes(ob))
            o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + OBJ1)); o.cpu.mem_write(0x6df + 0x56 * case['sel'] + 4 * case['own'], struct.pack('<I', o.POOL + 0x2200))
        if case['fn'] == 'fdee4':
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            o.cpu.mem_write(0x7d4, bytes(0x604)); end = 0xdce - case['room']; end = max(0x7f2 + 0x1e * case['qn'], end) if case['room'] > 0x80 else end
            wd(0xdce, end if case['room'] <= 0x80 else 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, 0, 2)
            rec = bytearray(0x50); struct.pack_into('<I', rec, 0, case['len']); struct.pack_into('<I', rec, 4, o.CODE + 0x30); rec[8] = case['rb8']; rec[9:9 + 0x40 - 9] = bytes.fromhex(case['dat'])[:0x40 - 9]
            o.cpu.mem_write(o.POOL + 0x2200, bytes(rec)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x2200)
        if case['fn'] in ('fe571', 'fe5ab', 'fe5f6'):
            wi = bytearray(prng(case['ds'] + 51, 0x80)); wi[3] = case['wf']; wi[0x10] = case['w10']; wi[0x11] = case['w11']
            o.cpu.mem_write(o.POOL + POOLREC, bytes(wi)); r[3] = wi[3]; o.cpu.mem_write(0x19e7, bytes([case['k19e7']]))
            lw = [0, bytes(wi[1:3]), b'\x07\x00'][case['l0w']]; o.cpu.mem_write(0, lw if isinstance(lw, bytes) else b'\x00\x00')
        if case['fn'] == 'fe65b':
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k, bytes([0, case['ids'][k], 0])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, 0, 2)
            own = case['f3']; wd(0x1972, own, 1); wd(0x6c6, case['cnt'], 1); o.cpu.mem_write(0x1f45, bytes(0x20))
            o.cpu.mem_write(0x97a + 0xf5 * own + 0xb9, bytes(case['rel']))
            for k in range(8): wd(o.POOL + 0xa000 + k * 0x1a35 + 0x19f1, (0x7cf + case['dv'][k]) & 0xffffffff, 4)
        if case['fn'] in ('fbea7', 'fbebb', 'fbed7', 'fbee6'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['ids'][k]])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, 0, 2)
            wd(0x1969, o.POOL + OBJ1, 4); ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50)); o.cpu.mem_write(o.POOL + OBJ1, bytes(ob))
        if case['fn'] in ('fdfe5', 'fe02a', 'fe03c', 'fe055', 'fe070'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            o.cpu.mem_write(0x7d4, bytes(0x604))
            for k in range(case['qn']): o.cpu.mem_write(0x7f2 + 0x1e * k, bytes(case['qe'][k]))
            wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, case['l0q'], 2)
        if case['fn'] == 'ff629':
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['ids'][k]])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, 0, 2)
            wd(0x6c6, 1, 1); wd(0x1972, case['own'], 1); wd(0x19e9, case['k19e9'], 1); wd(0x3cda, case['d3cda'], 4); wd(0x1ca9, case['v1ca9'], 4)
            for j in range(8):
                kind, dl, om = case['ent'][j]; base = 0x97a + 0x34 + 0xe * j
                o.cpu.mem_write(base, bytes([kind])); wd(base + 1, (case['d3cda'] + dl) & 0xffffffff, 4); o.cpu.mem_write(base + 9, bytes([case['own'] if om else (case['own'] + 1) & 255]))
            txt = bytes.fromhex(case['txt'])
            for k in range(4):
                blk = bytearray(); blk.append(case['nv'][k])
                for v in range(case['nv'][k]): blk += txt[(k * 3 + v) * 5:(k * 3 + v) * 5 + 1 + v % 4] + b'\xff'
                o.cpu.mem_write(o.POOL + 0x2200 + 0x40 * k, bytes(blk)); wd(0xefb + 0x13 * k, o.POOL + 0x2200 + 0x40 * k, 4); wd(0xefb + 0x13 * k + 4, case['eff'][k], 4)
            font = bytearray(prng(case['ds'] + 70, 0x200 + 32))
            for i in range(256): struct.pack_into('<H', font, 2 * i, 0x200 + (i % 8) * 4)
            o.cpu.mem_write(o.POOL + 0x2400, bytes(font)); wd(0x19ee, o.POOL + 0x2400, 4)
        if case['fn'] == 'f11b4e': o.cpu.mem_write(0x1c4e, bytes([case['k4e']])); o.cpu.mem_write(o.SEG, bytes(prng(case['map'] + 3, 8192))); o.cpu.mem_write(0x1906, bytes([case['r1']])); o.cpu.mem_write(0x190a, bytes([case['r2']]))
        if case['fn'] in ('f677f', 'f6d4a'): o.cpu.mem_write(0x68e, struct.pack('<I', 0xc00000))
        if case['fn'] in ('f6ba3', 'f6c04', 'f6c66', 'f6cd6'):
            rr = prng(case['bk'], 0x400); bank = bytearray(0x400)
            for ix in range(0x100): struct.pack_into('<H', bank, 2 * ix, 0x200 + 0x40 * (ix % 8))
            for sp in range(8):
                base = 0x200 + 0x40 * sp; w_, h_ = 1 + rr[sp * 3] % 8, 1 + rr[sp * 3 + 1] % 5; bank[base] = rr[sp * 3 + 2] % 4; struct.pack_into('<I', bank, base + 1, w_ | (h_ << 8))
                for q in range(w_ * h_): v = rr[0x40 + sp * 0x30 + q]; bank[base + 3 + q] = 0 if v % 10 < 3 else (v | 1)
            o.cpu.mem_write(o.POOL + 0x2a00, bytes(bank)); o.cpu.mem_write(0x19ee, struct.pack('<I', o.POOL + 0x2a00)); o.cpu.mem_write(0x68e, struct.pack('<I', 0xc00000)); o.cpu.mem_write(o.POOL + 0x2e00, bytes.fromhex(case['tbl'])); o.cpu.reg_write(UC_X86_REG_EBP, o.POOL + 0x2e00)
            if case['fn'] in ('f6c66', 'f6cd6'):
                tx = bytearray(); n_ = 1 + rr[0x3f0] % 6
                for q in range(n_):
                    v = rr[0x3e0 + q]
                    if v % 16 == 0: tx.append(0x0d)
                    elif v % 23 == 0: tx += bytes([0x7e, v % 8])
                    else: tx.append(0x20 + v % 0x5e)
                tx.append(0xff); o.cpu.mem_write(o.POOL + 0x2200, bytes(tx)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x2200)
        if case['fn'] == 'f5ce': o.cpu.mem_write(0x1c4e, bytes([case['k4e']])); o.cpu.mem_write(0, bytes([case['l0']]))
        if case['fn'] in ('f7a1', 'f7d0', 'f862', 'f8c9'):
            o.cpu.mem_write(0x19ea, bytes([case['k19ea']])); o.cpu.mem_write(0x19e7, bytes([case['b19e7']]))
            for k in range(8): o.cpu.mem_write(o.POOL + 0xa000 + k * 0x1a35 + 0x19f1, struct.pack('<I', (0x7cf + case['v7cf']) & 0xffffffff))
            if case['fn'] == 'f7d0': o.cpu.mem_write(0xefb + 0x13 * 3 + 0x11, bytes([5, 5 if case['seq'] else 6]))
            if case['fn'] == 'f7d0': o.cpu.mem_write(0xefb + 0x13 * 1 + 0x11, bytes([7, 7 if case['seq'] else 8]))
            if case['fn'] == 'f7d0': o.cpu.mem_write(0xefb + 0x13 * 2 + 0x11, bytes([9, 9 if case['seq'] else 4]))
        if case['fn'] == 'f4473':
            o.cpu.mem_write(0x1cad, struct.pack('<I', case['cad'])); o.cpu.mem_write(0x6c6, bytes([case['k6c6']])); o.cpu.mem_write(0x1972, bytes([case['own']])); o.cpu.mem_write(0x1cb5, bytes(case['lst']))
            for k in range(4): o.cpu.mem_write(0x1ccb + 0x16 * k, bytes([case['t'][k]]))
        if case['fn'] == 'f44c0':
            ob = bytearray(o.cpu.mem_read(o.POOL + OBJ1, 0x50)); ob[0x19] = case['hp']; o.cpu.mem_write(o.POOL + OBJ1, bytes(ob))
            o.cpu.mem_write(0x1ec6, bytes([case['k1ec6']])); o.cpu.mem_write(0x1974, struct.pack('<I', o.POOL + OBJ1 if case['p1974'] else 0)); o.cpu.mem_write(0x1cad, struct.pack('<I', case['cad'])); o.cpu.mem_write(0x19ea, bytes([case['k9ea']])); o.cpu.mem_write(0x1972, bytes([case['own']])); o.cpu.mem_write(0x6ba, struct.pack('<I', case['b6ba']))
        if case['fn'] == 'f42bb':
            o.cpu.mem_write(0x6c6, bytes([case['cnt']])); o.cpu.mem_write(0x1c4e, bytes([case['k4e']])); o.cpu.mem_write(0x1c4f, bytes([case['k4f']])); o.cpu.mem_write(0x19ea, bytes([case['k9ea']]))
            o.cpu.mem_write(0x2730, bytes(0x30)); o.cpu.mem_write(0x273f, bytes([case['g273'] & 255, case['g274'] & 255])); o.cpu.mem_write(o.FS + 0x10, bytes([0]))
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); struct.pack_into('<I', tb, 0x3b, o.CODE + 0x30); o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] == 'f59de':
            o.cpu.mem_write(0x1972, bytes([case['own']])); o.cpu.mem_write(0x1c87, bytes([case['b87']]))
        if case['fn'] in ('f5593', 'f64ad', 'f6a73', 'f6acd', 'f5969', 'f59de', 'f5f2'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['qid'] if k == 0 else (case['qid'] + 1)])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, case['l0q'], 2)
            if case['fn'] == 'f5f2':
                for k in range(case['qn']): o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['ids'][k]]))
        if case['fn'] in ('f5cc2', 'f5da5', 'f5df6'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v & ((1 << (8 * n)) - 1)).to_bytes(n, 'little'))
            wd(0x1c35, case['k35'], 2); wd(0x193a, case['d193a'], 4); wd(0x1969, o.POOL + OBJ1, 4); wd(0xed20, case['ed20'], 4); wd(0x1c87, case['b87'], 1)
            o.cpu.mem_write(o.POOL + OBJ1 + 0x19, bytes([case['hp']])); o.cpu.mem_write(o.POOL + OBJ1 + 0x0e, bytes([0])); o.cpu.mem_write(o.POOL + OBJ1, bytes([case['sel'] % 3]))
            o.cpu.mem_write(0x7d4, bytes(0x604)); [o.cpu.mem_write(0x7f2 + 0x1e * k + 1, bytes([case['qid'] if k == 0 else (case['qid'] + 1)])) for k in range(case['qn'])]; wd(0xdce, 0x7f2 + 0x1e * case['qn'], 4); wd(0x0, case['l0q'], 2)
        if case['fn'] == 'fdc91':
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x45] = 1 + tb[0x45] % 200; tb[0x4a] %= tb[0x45] + 1; tb[0x51] %= 3; tb[0x52] %= 3; tb[0x4c] %= 0x30; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
            rr = bytearray(o.cpu.mem_read(o.POOL + POOLREC, 0x50))
            if case['sel'] & 1: rr[0x1a] = rr[0x19]
            if case['sel'] & 2: rr[0x19] = 1 + rr[0x19] % 100; rr[0x1a] = 0
            o.cpu.mem_write(o.POOL + POOLREC, bytes(rr))
        if case['fn'] in ('fde0c', 'fce9d', 'fcf07', 'fcf41'):
            seg = bytearray(prng(case['map'] + 3, 8192)); o.cpu.mem_write(0x1902, struct.pack('<I', 1024))
            if case['sel'] & 2:
                for t in range(1024): seg[t * 8 + 2] = seg[t * 8 + 3] = ((seg[t * 8 + 2] & 15) * 17 if case['sel'] & 1 == 0 else seg[t * 8 + 2])
            o.cpu.mem_write(o.SEG, bytes(seg))
            if case['sel'] & 1:   # sparse planes: a cell in sixteen holds an object
                for k in range(4): o.cpu.mem_write(o.POOL + 0x4000 + 0x1000 * k, b''.join(struct.pack('<I', OBJ1 if b % 16 == 0 else 0) for b in prng(case['map'] + k, 1024)))
        if case['fn'] == 'fs14b' and case['sel'] != 0:
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x46] = 5; tb[0x74] = 0xf; tb[0x40] &= ~0x20 & 0xff; tb[0x41] &= 0x7f; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] in ('fs187', 'fs16b'): o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffff0000) | ((((case['regs'][0] >> 8) & 0xff) % 3) << 8))
        if case['fn'] not in ('f42bb', 'f4473', 'ff629', 'fe65b'): o.cpu.mem_write(0x6c6, bytes(1))
        o.cpu.mem_write(0x6c9, bytes(1)); o.cpu.mem_write(0x6c9, bytes(1)); o.cpu.mem_write(0x3e87, bytes(4))
        if case['fn'] == 'f7a03':
            q = prng(case['ds'] + 13, 8); o.cpu.mem_write(0x274e, bytes([q[0], q[1]])); o.cpu.mem_write(0x2791, bytes([q[2], q[3]]))
        if case['fn'] == 'fs18b':
            a1, a2 = prng(case['ds'] + 11, 0x3000), prng(case['ds'] + 12, 0x3000)
            blk = bytes(((a1[k] | a2[k]) if k % 3 == 0 else (a1[k] & a2[k])) & (0 if k % 0x84 in (0x59, 0x17) else 0xff) for k in range(0x3000))
            o.cpu.mem_write(0x5000, blk); o.cpu.mem_write(0x3e87, struct.pack('<I', 0x5000)); o.cpu.mem_write(0x6c9, bytes([1 + case['sel'] % 4])); o.cpu.mem_write(0x6c6, bytes([1 + case['g273'] % 4]))
        if case['fn'] == 'fc439':
            tf = struct.unpack('<I', prng(case['tm'], 0x100)[0x40:0x44])[0]; o.cpu.reg_write(UC_X86_REG_EBX, case['bm']); o.cpu.reg_write(UC_X86_REG_EAX, (tf & case['bm']) if case['sel'] else case['regs'][0])
        if case['fn'] == 'fd157': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ2)
        if case['fn'] == 'fc4cd': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] == 'f68bd': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ2)
        o.cpu.mem_write(0x1f0a, bytes(0x3b))
        if case['fn'] == 'fcccb': o.cpu.mem_write(0x1f70, prng(case['ds'] + 5, 0x1090))
        if case.get('z19'): o.cpu.mem_write(0x19ea, b'\x00')
        if os.environ.get('TRACE'): o.cpu.hook_add(__import__('unicorn').UC_HOOK_CODE, lambda uc, ad, sz, dd: print(hex(ad - o.CODE), hex(uc.reg_read(UC_X86_REG_ESI)), hex(uc.reg_read(UC_X86_REG_ECX))))
        try: o.run(o.CODE + entry)
        except Exception: print('FAIL', all_cases.index(case), case['fn'], case['regs'], hex(o.cpu.reg_read(UC_X86_REG_EIP) - o.CODE), [hex(o.cpu.reg_read(q)) for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_EDI, UC_X86_REG_ESI)]); raise
        fl = reg(UC_X86_REG_EFLAGS)
        if case['fn'] == 'f616f': r[3] = o.cpu.mem_read(o.POOL + POOLREC + 3, 1)[0]
        extra42 = (bytes(o.cpu.mem_read(0x2730, 0x30)) + bytes(o.cpu.mem_read(o.FS + 0x10, 1)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0x8000 + 0x60 * k, 0x20)) for k in range(case['nch']))) if case['fn'] == 'f42bb' else (bytes(o.cpu.mem_read(0xc00000, 0x8000)) if case['fn'] in ('f6ba3', 'f6c04', 'f6c66', 'f6cd6', 'f677f', 'f6d4a') else None) or ((bytes(o.cpu.mem_read(0x3cd0, 0x10)) + bytes(o.cpu.mem_read(0xdd4, 1))) if case['fn'] in ('f5f2', 'f7d0') else (bytes(o.cpu.mem_read(0x3cd0, 0x10)) + bytes(o.cpu.mem_read(0x7d4, 0x1cc)) if case['fn'] == 'ff629' else None)) or (bytes(o.cpu.mem_read(0xed20, 4)) + bytes(o.cpu.mem_read(0x1ccb + 0x16 * case['own'], 1)) if case['fn'] == 'f44c0' else b'')
        out = (bytes(o.cpu.mem_read(o.POOL + POOLREC, 0x80)) + bytes(o.cpu.mem_read(o.POOL + OBJ1, 0x50)) + bytes(o.cpu.mem_read(o.POOL + OBJ2, 0x50)) + bytes(o.cpu.mem_read(0x1c9f, 1)) + bytes(o.cpu.mem_read(0x512, 4)) + bytes(o.cpu.mem_read(0x50e, 4)) + bytes(o.cpu.mem_read(0x1ccb + 0x16 * r[3], 1)) + bytes(o.cpu.mem_read(0x525, 2)) + bytes(o.cpu.mem_read(0x1c61, 1)) + b''.join(bytes(o.cpu.mem_read(a, n)) for a, n in DS_WINDOWS) + bytes(o.cpu.mem_read(0x1a10, 0x10)) + bytes(o.cpu.mem_read(o.FS + 0x13d, 1)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0xa000 + k * 0x1a35 + 0x170, 0x40)) for k in range(3)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0xa000 + k * 0x1a35 + 0x70, 0x10)) for k in range(8)) + bytes(o.cpu.mem_read(0x1ca2, 0x20)) + bytes(o.cpu.mem_read(0x1f0a, 0x1fa)) + bytes(o.cpu.mem_read(0x4c0, 0x110)) + bytes(o.cpu.mem_read(0, 0x14)) + bytes(o.cpu.mem_read(0xf0b0, 0x10)) + bytes(o.cpu.mem_read(0x40, 0x20)) + bytes(o.cpu.mem_read(0x27f0, 0x60)) + (bytes(o.cpu.mem_read(0x7d4, 0x604)) if case['fn'] in ('fdec8', 'fdf2a', 'fdf7e', 'f5af6', 'f5b55', 'f5cc2', 'f5df6', 'f5da5', 'f5e1c', 'f5593', 'f64ad', 'f6a73', 'f6acd', 'f5969', 'f59de', 'f5f2', 'f7a1', 'f7d0', 'f862', 'f8c9', 'fdfe5', 'fe02a', 'fe03c', 'fe055', 'fe070', 'fdee4', 'fe5e6', 'fe621', 'fe636', 'fe65b', 'fbea7', 'fbebb', 'fbed7', 'fbee6') else bytes(0x604)) + bytes(o.cpu.mem_read(o.FS + 0x17f, 4)) + bytes(o.cpu.mem_read(o.POOL + 0x2800, 0x100))
               + struct.pack('<IIIII', *[rel(reg(q)) for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI)]) + bytes([(fl >> 6) & 1, fl & 1]) + bytes(o.cpu.mem_read(0x97a + 0xf5 * r[3], 0xf5)) + (b''.join(bytes(o.cpu.mem_read(o.POOL + 0x4000 + 0x1000 * k, 0x1000)) for k in range(4)) if case['fn'] == 'fd2d1' else b'') + b''.join(log)) + extra42
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'actions': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--funcs', default=','.join(FUNCS))
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game, args.funcs.split(','))) + '\n', encoding='utf-8')
