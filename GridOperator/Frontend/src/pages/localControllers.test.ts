/**
 * The local controllers drawn, in a document of happy-dom, against a
 * stand-in grid operator: a row each, with the connections and subscriptions
 * of its VEN below it, and one added - whose secret is said once, above the
 * list, and gone after a Reload.
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
                         created: '2026-10-19T08:00:00Z', lastSeen: null, limits: [], reports: 0, connections: [], subscriptions: [], targets: [] };

        controllers.push(added);

        return { ...added, credentials: { tokenURL: 'http://127.0.0.1:2358/openadr3/auth/token', vtnURL: 'http://127.0.0.1:2358/openadr3/',
                                          clientID: asked['venName'], clientSecret: 'the-secret-said-once' } };

    }

    return { tokenURL: 'http://127.0.0.1:2358/openadr3/auth/token', vtnURL: 'http://127.0.0.1:2358/openadr3/', localControllers: controllers };

}


describe('the local controllers', () => {

    it('draw a row for every one, and offer nothing to somebody who may only look', async () => {

        controllers = [ { venID: 'V0', venName: 'lc-nord', chargingPoolId: 'DE*GEF*P2', gridConnectionPointId: 'GCP-1',
                          created: '2026-10-18T08:00:00Z', lastSeen: '2026-10-19T07:59:00Z', limits: [ 'E1' ], reports: 2,
                          connections: [], subscriptions: [], targets: [] } ];

        const root = await open(localControllersPage, '/openADR/localControllers', [ 'openadr:read', 'poi:read' ],
                                gridOperator, root => root.querySelectorAll('table.grid-table tbody tr.local-controller').length === 1);

        assert.match(root.querySelector('table.grid-table tbody tr.local-controller')!.textContent!, /lc-nord.*DE\*GEF\*P2.*GCP-1/s);
        assert.match(root.querySelector('table.grid-table tbody tr.ven-links')!.textContent!, /Connections\s*None.*Subscriptions\s*None/s);
        assert.equal(root.querySelector('#new-local-controller-form'), null);
        assert.ok([ ...root.querySelectorAll<HTMLButtonElement>('td.actions button') ].every(button => button.disabled));

    });

    it('show below each its connections, with their binding and where they stand, and its subscriptions, over a connection or to a webhook', async () => {

        controllers = [ { venID: 'V0', venName: 'lc-paradies', chargingPoolId: 'DE*GEF*P1', gridConnectionPointId: 'GCP-1',
                          created: '2026-10-18T08:00:00Z', lastSeen: null, limits: [], reports: 0, targets: [],
                          connections: [
                              { id: 'C1', login: 'lc-paradies-messages', description: null, transport: 'wss', binding: 'messages', enabled: true,
                                status: 'waiting', said: "Waiting for 'lc-paradies-messages' to connect.", since: '2026-10-19T08:00:00Z', in: false, notificationsWaiting: 3 },
                              { id: 'C2', login: 'lc-paradies-rest', description: null, transport: 'ssh', binding: 'rest', enabled: true,
                                status: 'connected', said: "'lc-paradies-rest' is connected over SSH.", since: '2026-10-19T08:00:00Z', in: true, notificationsWaiting: 0 },
                              { id: 'C3', login: 'lc-paradies-old', description: null, transport: 'ws', binding: 'messages', enabled: false,
                                status: 'waiting', said: "'lc-paradies-old' is not let in: this connection is switched off.", since: null, in: false, notificationsWaiting: 0 }
                          ],
                          subscriptions: [
                              { id: 'S1', clientName: 'over its connection', created: '2026-10-19T08:00:00Z', toldOver: 'C1',
                                objectOperations: [ { objects: [ 'EVENT' ], operations: [ 'CREATE', 'UPDATE' ], callbackUrl: 'wss://vtn.openadr.messages/', callback: 'connection' } ] },
                              { id: 'S2', clientName: 'to a webhook', created: null, toldOver: null,
                                objectOperations: [ { objects: [ 'REPORT' ], operations: [ 'DELETE' ], callbackUrl: 'https://lc.example.org/hook', callback: 'webhook' } ] }
                          ] } ];

        const root = await open(localControllersPage, '/openADR/localControllers', [ 'openadr:read', 'poi:read' ],
                                gridOperator, root => root.querySelectorAll('li.ven-connection').length === 3);

        const connection = (id: string) => root.querySelector(`li.ven-connection[data-id="${id}"]`)!.textContent!.replace(/\s+/g, ' ');

        assert.match(connection('C1'), /wss messages on waiting 3 notification\(s\) waiting lc-paradies-messages/);
        assert.match(connection('C1'), /Waiting for 'lc-paradies-messages' to connect\./);
        assert.ok(root.querySelector('li.ven-connection[data-id="C1"] .chip.waiting.warn'), 'notifications that wait are not marked');
        assert.match(connection('C2'), /ssh REST in a tunnel on in now lc-paradies-rest/);
        assert.doesNotMatch(connection('C2'), /notification/, 'a connection of the REST API said notifications wait');
        assert.ok(root.querySelector('li.ven-connection[data-id="C2"] .chip.ok'), 'a connection in now is not marked as in');
        assert.match(connection('C3'), /ws messages off waiting 0 notification\(s\) waiting lc-paradies-old/);

        const subscriptions = [ ...root.querySelectorAll('li.ven-subscription') ].map(one => one.textContent!.replace(/\s+/g, ' '));

        assert.equal(subscriptions.length, 2);
        assert.match(subscriptions[0]!, /CREATE ?UPDATE EVENT over the connection lc-paradies-messages wss:\/\/vtn\.openadr\.messages\//);
        assert.match(subscriptions[0]!, /over its connection/);
        assert.match(subscriptions[1]!, /DELETE REPORT to the webhook https:\/\/lc\.example\.org\/hook/);

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
