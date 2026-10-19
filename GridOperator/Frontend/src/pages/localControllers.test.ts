/**
 * The local controllers drawn, in a document of happy-dom, against a
 * stand-in grid operator: a row each, and one added - whose secret is said
 * once, above the list, and gone after a Reload.
 */

import { asked, field, leave, open, submit, type, until, type Asked } from '../../test/gridOperator.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { localControllersPage } = await import('./localControllers.ts');

after(() => leave());


let controllers: Record<string, unknown>[] = [];

function gridOperator({ method, path, body }: Asked): unknown {

    if (path === '/gridConnectionPoints')
        return { gridOperatorId: 'DE*SWJ', voltageLevels: [], connectionTypes: [],
                 gridConnectionPoints: [ { '@id': 'GCP-1', localControllers: [], limits: [] } ] };

    if (path !== '/openADR/localControllers')
        return undefined;

    if (method === 'POST') {

        const asked  = body as Record<string, string>;
        const added  = { venID: 'V1', venName: asked['venName'], chargingPoolId: asked['chargingPoolId'], gridConnectionPointId: asked['gridConnectionPointId'],
                         created: '2026-10-19T08:00:00Z', lastSeen: null, limits: [], reports: 0 };

        controllers.push(added);

        return { ...added, credentials: { tokenURL: 'http://127.0.0.1:2358/openadr3/auth/token', vtnURL: 'http://127.0.0.1:2358/openadr3/',
                                          clientID: asked['venName'], clientSecret: 'the-secret-said-once' } };

    }

    return { tokenURL: 'http://127.0.0.1:2358/openadr3/auth/token', vtnURL: 'http://127.0.0.1:2358/openadr3/', localControllers: controllers };

}


describe('the local controllers', () => {

    it('draw a row for every one, and offer nothing to somebody who may only look', async () => {

        controllers = [ { venID: 'V0', venName: 'lc-nord', chargingPoolId: 'DE*GEF*P2', gridConnectionPointId: 'GCP-1',
                          created: '2026-10-18T08:00:00Z', lastSeen: '2026-10-19T07:59:00Z', limits: [ 'E1' ], reports: 2 } ];

        const root = await open(localControllersPage, '/openADR/localControllers', [ 'openadr:read', 'poi:read' ],
                                gridOperator, root => root.querySelectorAll('table.grid-table tbody tr').length === 1);

        assert.match(root.querySelector('table.grid-table tbody tr')!.textContent!, /lc-nord.*DE\*GEF\*P2.*GCP-1/s);
        assert.equal(root.querySelector('#new-local-controller-form'), null);
        assert.ok([ ...root.querySelectorAll<HTMLButtonElement>('td.actions button') ].every(button => button.disabled));

    });

    it('add one, say its secret once, and forget it on Reload', async () => {

        controllers = [];

        const root = await open(localControllersPage, '/openADR/localControllers', [ 'openadr:read', 'openadr:edit', 'poi:read' ],
                                gridOperator, root => root.querySelector('#new-local-controller-form') !== null);

        type(field(root, '#new-local-controller-form', 'venName'),         'lc-paradies');
        type(field(root, '#new-local-controller-form', 'chargingPoolId'),  'DE*GEF*P1');
        submit(root, '#new-local-controller-form');

        await until(() => root.querySelector('.secret-box') !== null, 'the secret was not said');

        assert.match(root.querySelector('.secret-box')!.textContent!, /the-secret-said-once/);
        assert.deepEqual(asked.find(one => one.method === 'POST')?.body,
                         { venName: 'lc-paradies', chargingPoolId: 'DE*GEF*P1', gridConnectionPointId: 'GCP-1' });
        assert.match(root.querySelector('table.grid-table tbody')!.textContent!, /lc-paradies/);

        root.querySelector<HTMLButtonElement>('.page-actions #reload')!.click();

        await until(() => root.querySelector('.secret-box') === null, 'the secret was said again after Reload');

    });

});
