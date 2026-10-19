/**
 * The grid connection points drawn, in a document of happy-dom, against a
 * stand-in grid operator: a row each, leading to the point, and a new one
 * added as POI 3 writes it - in W, from what was typed in kW, an emptied field
 * not given at all.
 */

import { asked, field, leave, open, refused, submit, type, until, type Asked } from '../../test/gridOperator.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { gridConnectionPointsPage } = await import('./gridConnectionPoints.ts');

after(() => leave());


let points: Record<string, unknown>[] = [];

function gridOperator({ method, path, body }: Asked): unknown {

    if (path !== '/gridConnectionPoints')
        return undefined;

    if (method === 'POST') {

        const point = body as Record<string, unknown>;

        if (points.some(one => String(one['@id']).toLowerCase() === String(point['@id']).toLowerCase()))
            return refused(409, `The grid operator 'DE*SWJ' has a grid connection point '${String(point['@id'])}' already.`);

        const added = { ...point, localControllers: [], limits: [] };
        points.push(added);
        return added;

    }

    return { gridOperatorId: 'DE*SWJ', gridConnectionPoints: points,
             voltageLevels: [ 'LowVoltage', 'MediumVoltage' ], connectionTypes: [ 'AC_ThreePhases', 'Unknown' ] };

}


describe('the grid connection points', () => {

    it('draw a row for every point, with the local controllers behind it', async () => {

        points = [ { '@id': 'GCP-1', name: { de: 'Paradies' }, voltageLevel: 'LowVoltage', contractedImportPower: 110000, localControllers: [ 'lc-paradies' ], limits: [] } ];

        const root = await open(gridConnectionPointsPage, '/gridConnectionPoints', [ 'poi:read' ],
                                gridOperator, root => root.querySelectorAll('table.grid-table tbody tr').length === 1);

        const row = root.querySelector('table.grid-table tbody tr')!;

        assert.match(row.textContent!, /GCP-1/);
        assert.match(row.textContent!, /Paradies/);
        assert.match(row.textContent!, /110\s*kW/);
        assert.match(row.textContent!, /lc-paradies/);
        assert.ok(row.querySelector('a[href$="/gridConnectionPoints/GCP-1"]'), 'a row does not lead to its point');
        assert.equal(root.querySelector('#new-point-form'), null, 'somebody who may only look is offered a form');

    });

    it('add a point as POI 3 writes it, and say what is refused', async () => {

        points = [ { '@id': 'GCP-1', localControllers: [], limits: [] } ];

        const root = await open(gridConnectionPointsPage, '/gridConnectionPoints', [ 'poi:read', 'poi:edit' ],
                                gridOperator, root => root.querySelector('#new-point-form') !== null);

        type(field(root, '#new-point-form', 'id'),        'GCP-1');
        submit(root, '#new-point-form');

        await until(() => /already/.test(root.querySelector('#new-point-error')!.textContent!), 'a refusal was not said');

        type(field(root, '#new-point-form', 'id'),        'GCP-2');
        type(field(root, '#new-point-form', 'importKW'),  '44');
        type(field(root, '#new-point-form', 'marketLocationIds'), '501, 502');
        submit(root, '#new-point-form');

        await until(() => asked.filter(one => one.method === 'POST').length === 2, 'the point was not sent');

        const sent = asked.filter(one => one.method === 'POST')[1]!.body as Record<string, unknown>;

        assert.equal(sent['@id'],                    'GCP-2');
        assert.equal(sent['contractedImportPower'],  44000);
        assert.deepEqual(sent['marketLocationIds'],  [ '501', '502' ]);
        assert.ok(!('contractedExportPower' in sent), 'an empty field was sent');
        assert.ok(!('nominalVoltage' in sent),        'an empty field was sent');

    });

});
