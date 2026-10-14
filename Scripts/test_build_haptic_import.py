"""Tests of build_haptic_import.py (variant expansion, shared / separate materials, several clips).

    python Scripts/test_build_haptic_import.py
"""
import copy, json, tempfile, unittest
from pathlib import Path

import build_haptic_import as bhi


class PlanTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        root = Path(self.dir.name)
        self.clips, self.sfx = root / 'clips', root / 'sfx'
        self.clips.mkdir(); self.sfx.mkdir()
        for name in ('a', 'b', 'c'):
            (self.clips / f'{name}.wav').write_bytes(b'')
        for name in ('S1', 'S2', 'S3'):
            (self.sfx / f'{name}.wav').write_bytes(b'')
        route = lambda at, **clip: [dict({'at': at, 'gain': 0.8}, **clip)]
        cues = {}
        for name, at in bhi.CUES.items():
            cues[name] = {'description': name, 'sfx': {'sounds': ['S1', 'S2'], 'volume': 1}, 'variation': {'pick': 'random'},
                          'variants': {'soft': {'haptics': route(at, clip='a')}, 'hard': {'haptics': route(at, clips=['b', 'c'])}}}
        self.table = {'clips': {'a': {'intensity': 0.5}, 'b': {'intensity': 1}, 'c': {'intensity': 0.25}},
                      'sounds': {'S1': {'intensity': 0.5}, 'S3': {}}, 'cues': cues}

    def tearDown(self):
        self.dir.cleanup()

    def plan(self, table=None):
        return bhi.plan(table or self.table, self.clips, self.sfx)

    def test_every_variant_becomes_its_game_cue_in_order(self):
        items, err = self.plan()
        self.assertEqual(err, [])
        self.assertEqual([i['cue'] for i in items], bhi.GAME_CUES)
        self.assertEqual(bhi.table_cue('guard_glove_hard'), 'guard_glove:hard')
        self.assertEqual(bhi.table_cue('hit_body_soft'), 'hit_body:soft')

    def test_a_variant_without_sfx_shares_the_cue_sfx(self):
        items, _ = self.plan()
        for item in items:
            self.assertEqual(item['sounds'], ['S1', 'S2'])
            self.assertEqual(item['volumes'], [0.5, 1.0])

    def test_separate_sfx_and_shared_haptics(self):
        table = copy.deepcopy(self.table)
        punch = table['cues']['punch_glove']
        del punch['sfx']
        punch['variants']['soft']['sfx'] = {'sound': 'S1'}
        punch['variants']['hard']['sfx'] = {'sound': 'S3', 'volume': 2}
        punch['haptics'] = [{'at': 'hand', 'gain': 1, 'clip': 'c'}]
        for v in punch['variants'].values():
            del v['haptics']
        items, err = self.plan(table)
        self.assertEqual(err, [])
        soft, hard = items[0], items[1]
        self.assertEqual((soft['sounds'], soft['volumes'], soft['clips'], soft['gain']), (['S1'], [0.5], ['c'], 1.0))
        self.assertEqual((hard['sounds'], hard['volumes'], hard['clips'], hard['gain']), (['S3'], [2.0], ['c'], 1.0))

    def test_a_variant_overrides_the_cue(self):
        # DEC-091: on the cue and on a variant is allowed; the variant's own field wins (Studio also leaves an
        # empty haptics list on the cue once every variant has its own).
        table = copy.deepcopy(self.table)
        punch = table['cues']['punch_glove']
        punch['variants']['hard']['sfx'] = {'sound': 'S3'}
        punch['haptics'] = []
        items, err = self.plan(table)
        self.assertEqual(err, [])
        self.assertEqual((items[0]['sounds'], items[1]['sounds']), (['S1', 'S2'], ['S3']))
        self.assertEqual(items[1]['clips'], ['b', 'c'])

    def test_materials_live_on_the_cue_or_on_every_variant(self):
        for mutate, expected in [
            # On one variant only (the other would have nothing).
            (lambda c: c['variants']['soft'].pop('haptics'), 'punch_glove: haptics is on hard; separate = on every variant'),
        ]:
            table = copy.deepcopy(self.table)
            mutate(table['cues']['punch_glove'])
            _, err = self.plan(table)
            self.assertTrue(any(expected in e for e in err), (expected, err))

    def test_multipliers_scale_only_what_is_inherited(self):
        table = copy.deepcopy(self.table)
        guard = table['cues']['guard_arm']
        guard['haptics'] = [{'at': 'hand', 'gain': 0.8, 'clip': 'a'}]
        for v in guard['variants'].values():
            del v['haptics']
        guard['variants']['soft'].update(sfxVolume=0.5, hapticsGain=0.5)
        items, err = self.plan(table)
        self.assertEqual(err, [])
        soft, hard = items[bhi.GAME_CUES.index('guard_arm_soft')], items[bhi.GAME_CUES.index('guard_arm_hard')]
        self.assertEqual(soft['volumes'], [0.25, 0.5])
        self.assertAlmostEqual(soft['gain'], 0.4)
        self.assertEqual((hard['volumes'], hard['gain']), ([0.5, 1.0], 0.8))
        table = copy.deepcopy(self.table)
        table['cues']['guard_arm']['variants']['hard']['hapticsGain'] = 0.5  # hard has its own haptics
        _, err = self.plan(table)
        self.assertTrue(any('guard_arm:hard: hapticsGain' in e for e in err), err)

    def test_several_clips_keep_their_order_and_intensities(self):
        items, _ = self.plan()
        self.assertEqual(items[1]['clips'], ['b', 'c'])
        self.assertEqual(items[1]['intensities'], [1.0, 0.25])
        self.assertEqual(items[0]['intensities'], [0.5])

    def test_variant_pick_overrides_the_cue_pick(self):
        table = copy.deepcopy(self.table)
        table['cues']['hit_body']['variants']['hard']['variation'] = {'pick': 'roundRobin'}
        items, _ = self.plan(table)
        self.assertEqual(items[-1]['pick'], 'roundRobin')
        self.assertEqual(items[-2]['pick'], 'random')

    def test_wrong_shapes_are_errors(self):
        for mutate, expected in [
            (lambda t: t['cues']['punch_body']['variants'].pop('hard'), 'punch_body: variants must be exactly'),
            (lambda t: t['cues']['hit_body']['variants']['soft']['haptics'][0].update(at='hand'), 'hit_body:soft: exactly one route'),
            (lambda t: t['cues']['punch_glove']['variants']['soft']['haptics'][0].update(clips=['a']), 'punch_glove:soft: exactly one route'),
            (lambda t: t['cues']['punch_glove']['variants']['hard']['haptics'][0].update(clips=['b', 'z']), 'clip z is not in clips'),
            (lambda t: t['cues']['guard_glove']['variants']['soft'].update(rampTo=2), 'guard_glove:soft: rampTo'),
            (lambda t: t['cues']['guard_glove'].update(variation={'pick': 'random', 'paired': True}), 'variation.paired'),
        ]:
            table = copy.deepcopy(self.table)
            mutate(table)
            _, err = self.plan(table)
            self.assertTrue(any(expected in e for e in err), (expected, err))

    def test_the_checked_in_table_plans_every_game_cue(self):
        items, err = bhi.plan(json.loads(bhi.CUES_PATH.read_text(encoding='utf-8')))
        self.assertEqual(err, [])
        self.assertEqual([i['cue'] for i in items], bhi.GAME_CUES)


if __name__ == '__main__':
    unittest.main()
