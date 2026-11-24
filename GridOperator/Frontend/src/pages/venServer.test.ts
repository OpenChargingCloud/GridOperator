/**
 * The page of the VEN server drawn, in a document of happy-dom, against a
 * stand-in grid operator: the server as it is, and who is connected - and
 * nobody, where nobody connected yet.
 */

import { open, type Asked } from '../../test/gridOperator.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { venServerPage } = await import('./venServer.ts');


function server(connected: string[], running = true): Record<string, unknown> {
    return {
        configuration:  { enabled: true, port: 2365, clientAuthentication: [ 'httpAuthOverTLS', 'tlsClientCertificate' ], ssh: { enabled: true } },
        state:          { running, tls: true, ssh: running, sshPort: 22 },
        connected
    };
}

function answering(Server: Record<string, unknown>) {
    return (asked: Asked) => asked.path === '/configuration/ven-server' ? Server : undefined;
}


describe('the page of the VEN server', () => {

    it('says the server is listening, and who is connected', async () => {

        await open(venServerPage, '/configuration/ven-server', [ 'openadr:read' ],
                   answering(server([ 'lc-1' ])), root => root.querySelector('#ven-connected') !== null);

        assert.match(document.querySelector('#ven-websocket')?.textContent ?? '', /listening on port 2365, over TLS/);
        assert.match(document.querySelector('#ven-ssh')?.textContent       ?? '', /served on port 22/);
        assert.match(document.querySelector('#ven-connected')?.textContent ?? '', /lc-1/);

    });

    it('says so, where the server does not listen and nobody is connected', async () => {

        await open(venServerPage, '/configuration/ven-server', [ 'openadr:read' ],
                   answering(server([], false)), root => root.querySelector('#ven-connected') !== null);

        assert.match(document.querySelector('#ven-websocket')?.textContent ?? '', /switched on, not listening/);
        assert.match(document.querySelector('#ven-ssh')?.textContent       ?? '', /not served/);
        assert.match(document.querySelector('#ven-connected')?.textContent ?? '', /No local controller is connected/);

    });

});
