/**
 * The limits drawn, in a document of happy-dom, against a stand-in grid
 * operator: a row each with its state, and one sent for the local controllers
 * behind a grid connection point - "From" left empty for now, and no point
 * chosen for every local controller.
 */

import { asked, field, leave, open, submit, type, until, change, type Asked } from '../../test/gridOperator.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { limitsPage } = await import('./limits.ts');

after(() => leave());


let limits: Record<string, unknown>[] = [];

function gridOperator({ method, path, body }: Asked): unknown {

    switch (path) {

        case '/openADR':
            return { vtnURL: 'http://127.0.0.1:2358/openadr3/', tokenURL: 'http://127.0.0.1:2358/openadr3/auth/token',
                     program: { programName: 'grid-capacity' }, kinds: [ 'IMPORT_CAPACITY_LIMIT', 'EXPORT_CAPACITY_LIMIT' ], unit: 'KW',
                     maxDurationMinutes: 10080, localControllers: 1, signedIn: 0, activeLimits: 0, scheduledLimits: 0, reports: 0, file: 'x' };

        case '/openADR/reports':
            return { capacity: 500, reports: [] };

        case '/gridConnectionPoints':
            return { gridOperatorId: 'DE*SWJ', voltageLevels: [], connectionTypes: [],
                     gridConnectionPoints: [ { '@id': 'GCP-1', localControllers: [ 'lc-1' ], limits: [] },
                                             { '@id': 'GCP-2', localControllers: [],        limits: [] } ] };

        case '/openADR/limits':
            if (method === 'POST') {
                const sent  = body as Record<string, unknown>;
                const limit = { id: `E${limits.length + 1}`, name: null, kind: sent['kind'], limitKW: sent['limitKW'], start: '2026-10-19T08:00:00Z',
                                end: '2026-10-19T09:00:00Z', state: 'active', priority: null, gridConnectionPointId: sent['gridConnectionPointId'] ?? null,
                                localControllers: [ 'lc-1' ], sentBy: 'dispatcher1', created: '2026-10-19T08:00:00Z', event: {} };
                limits.unshift(limit);
                return limit;
            }
            return { timestamp: '2026-10-19T08:00:00Z', limits };

    }

    return undefined;

}


describe('the limits', () => {

    it('send one for the local controllers behind a point, as numbers, from now', async () => {

        limits = [];

        const root = await open(limitsPage, '/openADR/limits', [ 'openadr:read', 'openadr:run', 'poi:read' ],
                                gridOperator, root => root.querySelector('#new-limit-form') !== null);

        const points = [ ...field<HTMLSelectElement>(root, '#new-limit-form', 'gridConnectionPointId').options ].map(option => option.value);

        assert.deepEqual(points, [ '', 'GCP-1' ], 'a point with nobody behind it is offered');

        change(field<HTMLSelectElement>(root, '#new-limit-form', 'gridConnectionPointId'), 'GCP-1');
        type(field(root, '#new-limit-form', 'limitKW'), '44');
        submit(root, '#new-limit-form');

        await until(() => root.querySelectorAll('table.grid-table tbody tr').length === 1, 'the limit sent is not in the list');

        assert.deepEqual(asked.find(one => one.method === 'POST')?.body,
                         { kind: 'IMPORT_CAPACITY_LIMIT', limitKW: 44, durationMinutes: 60, gridConnectionPointId: 'GCP-1' });
        assert.match(root.querySelector('table.grid-table tbody tr')!.textContent!, /active/);

    });

    it('offer nothing to send to somebody who may only look', async () => {

        const root = await open(limitsPage, '/openADR/limits', [ 'openadr:read' ],
                                gridOperator, root => root.querySelector('.cards') !== null);

        assert.equal(root.querySelector('#new-limit-form'), null);
        assert.match(root.textContent!, /may look at the limits but not send or cancel one/);

    });

});
