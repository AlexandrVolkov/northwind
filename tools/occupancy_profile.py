"""Attach a hash-gated initial common-registration profile to imported scenarios."""
import hashlib
import json
from pathlib import Path

MODES={'overwrite','claim_empty','excluded_common_flag','deferred_first_update'}


def attach_initial_occupancy(scenario, scenario_name, read, profile=None):
    result={'status':'unavailable','reason':'Source registration profile does not match',
            'scope':'Initial common registration geometry; not a full game tick.'}
    scenario['format_version']=2
    scenario['initial_occupancy']=result
    for slot in scenario['slots']:
        for obj in slot['objects']:
            obj.update({'occupancy_mode':'unverified','runtime_reference':0})
    if profile is None:
        path=Path(__file__).resolve().parents[1]/'docs/occupancy-profile.json'
        if not path.exists():
            result['reason']='Registration profile is missing';return result
        profile=json.loads(path.read_text(encoding='utf-8'))
    entry=profile['scenarios'].get(scenario_name)
    if entry is None:
        result['reason']='Scenario is outside the verified profile';return result
    try:
        matches=all(hashlib.sha256(read(name)).hexdigest()==profile['source_sha256'][name] for name in entry['source_files'])
    except ValueError:
        matches=False
    if not matches:return result
    pending=[]
    for slot in scenario['slots']:
        for obj in slot['objects']:
            key=f'{obj["template_file"]}:{obj["template_index"]}'
            mode=profile['templates'].get(key)
            if mode not in MODES:raise ValueError('Unrecognized registration policy')
            reference=slot['slot_index']*65536+1+obj['file_offset']
            pending.append((obj,mode,reference))
    if len(pending)!=entry['object_count']:raise ValueError('Registration profile count differs from source records')
    for obj,mode,reference in pending:obj.update({'occupancy_mode':mode,'runtime_reference':reference})
    result.update({'status':'ready','reason':'','expected_sha256':entry['sha256'],
                   'object_count':entry['object_count'],'claim_conflicts':entry['claim_conflicts']})
    return result
