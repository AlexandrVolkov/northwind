import hashlib
from pathlib import Path
import sys
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from occupancy_profile import attach_initial_occupancy


class OccupancyProfileTests(unittest.TestCase):
    def fixture(self):
        scenario={'slots':[{'slot_index':0,'objects':[{'file_offset':0,'template_file':'TEST.DAT','template_index':0}]},
                           {'slot_index':3,'objects':[{'file_offset':49,'template_file':'TEST.DAT','template_index':0}]}]}
        blobs={'TEST.DAT':b'source'}
        profile={'source_sha256':{'TEST.DAT':hashlib.sha256(blobs['TEST.DAT']).hexdigest()},
                 'templates':{'TEST.DAT:0':'overwrite'},
                 'scenarios':{'TEST.SCN':{'source_files':['TEST.DAT'],'object_count':2,'sha256':'expected','claim_conflicts':0}}}
        return scenario,blobs,profile

    def test_zero_file_offset_is_nonzero_runtime_reference_and_slot_stride_is_preserved(self):
        scenario,blobs,profile=self.fixture()
        result=attach_initial_occupancy(scenario,'TEST.SCN',blobs.__getitem__,profile)
        self.assertEqual(result['status'],'ready')
        self.assertEqual([s['objects'][0]['runtime_reference'] for s in scenario['slots']],[1,3*65536+50])
        self.assertEqual(scenario['format_version'],2)

    def test_changed_source_disables_and_clears_previously_attached_registration(self):
        scenario,blobs,profile=self.fixture()
        attach_initial_occupancy(scenario,'TEST.SCN',blobs.__getitem__,profile)
        blobs['TEST.DAT']=b'modified'
        result=attach_initial_occupancy(scenario,'TEST.SCN',blobs.__getitem__,profile)
        self.assertEqual(result['status'],'unavailable')
        for slot in scenario['slots']:
            self.assertEqual(slot['objects'][0]['runtime_reference'],0)
            self.assertEqual(slot['objects'][0]['occupancy_mode'],'unverified')

    def test_unknown_scenario_is_not_assumed_compatible(self):
        scenario,blobs,profile=self.fixture()
        self.assertEqual(attach_initial_occupancy(scenario,'OTHER.SCN',blobs.__getitem__,profile)['status'],'unavailable')


if __name__ == '__main__':unittest.main()
