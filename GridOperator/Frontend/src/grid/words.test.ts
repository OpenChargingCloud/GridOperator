/**
 * How many of something there are, said as English says it - as the grid
 * operator's command line and log say it, and WWCP_Node's NodeWording.Plural.
 */

import { strict as assert } from 'node:assert';
import { describe, it }     from 'node:test';

import { counted, plural } from './words.ts';

describe('counted', () => {

    it('says one in the singular, and none or more in the plural', () => {
        assert.equal(counted(1, 'local controller'),        '1 local controller');
        assert.equal(counted(0, 'local controller'),        '0 local controllers');
        assert.equal(counted(3, 'grid connection point'),   '3 grid connection points');
    });

    it('makes the plural as English does', () => {
        assert.equal(plural('registration authority'),  'registration authorities');
        assert.equal(plural('key'),                     'keys');
        assert.equal(plural('switch'),                  'switches');
        assert.equal(plural('CPO'),                     'CPOs');
    });

});
