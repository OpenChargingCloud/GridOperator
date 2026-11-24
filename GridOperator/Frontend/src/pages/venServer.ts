import { venServerAPI as api, type VENServer } from '../api/client';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, render, repeat } from '@node/view';

/** How a client may authenticate, in a few words. */
const IN_WORDS: Record<string, string> = {
    httpAuthWithoutTLS:    'a password, without TLS',
    httpAuthOverTLS:       'a password, over TLS',
    tlsClientCertificate:  'a TLS client certificate'
};

/**
 * The server the local controllers connect to - their VENs, speaking OpenADR
 * over a WebSocket or SSH, the REST API in a tunnel or in messages - as it
 * is: whether it listens, how one may sign in, and who is connected now.
 *
 * Read-only: the server is changed in the "webSocketServer" section of the
 * file, and the grid operator started again. Who may connect is changed on
 * the pages below it - the logins, the accepted chains and the connections,
 * which are every WebSocket server's pages. A local controller that signs in
 * over HTTPS, with the client secret of OpenADR 3, is on the page of the
 * local controllers.
 */
export const venServerPage: Page = {

    title: 'VEN server',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/ven-server',
            title:     'VEN server',
            subtitle:  'the server the local controllers connect to, over a WebSocket or SSH, and who is connected.',
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        let cancelled = false;


        function draw(server: VENServer): void {

            const configuration = server.configuration;

            render(content, html`

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-server"></i> The server</h2>

                        <div class="kv">
                            <span class="k">WebSocket</span>
                            <span class="v" id="ven-websocket">
                                ${server.state.running
                                      ? html`<span class="badge ok">listening</span> on port ${configuration.port ?? '-'}${server.state.tls ? ', over TLS' : ', without TLS'},
                                             as <code>openadr3.1-rest</code> and <code>openadr3.1</code>`
                                      : configuration.enabled === true
                                            ? html`<span class="badge warn">switched on, not listening</span>`
                                            : html`<span class="badge">switched off</span>`}
                            </span>
                            <span class="k">SSH</span>
                            <span class="v" id="ven-ssh">
                                ${server.state.ssh
                                      ? html`<span class="badge ok">served</span> on port ${server.state.sshPort ?? '-'}, as the subsystems
                                             <code>openadr-rest@open.charging.cloud</code> and <code>openadr@open.charging.cloud</code>`
                                      : html`<span class="badge">not served</span>`}
                            </span>
                            <span class="k">Signing in</span>
                            <span class="v">${(configuration.clientAuthentication ?? []).map(way => IN_WORDS[way] ?? way).join(', ') || '-'}</span>
                            <span class="k">Revocation</span>
                            <span class="v">${configuration.checkCertificateRevocation ? 'a client certificate is checked against its CRL' : 'not checked'}</span>
                        </div>

                        <p class="hint">
                            Changed in the <code>webSocketServer</code> section of the file, and the grid operator started again.
                            Who may connect: the <a href="${toURL('/configuration/ven-server/logins')}">logins</a>,
                            the <a href="${toURL('/configuration/ven-server/trust')}">accepted chains</a> and the
                            <a href="${toURL('/configuration/connections')}">connections</a> - a local controller is
                            written down as both, a connection of the type "VEN" that lets it in, with its binding and the
                            VEN it is taken as, and a login it signs in with. One that signs in over HTTPS, with a client
                            secret, is on the page of the <a href="${toURL('/openADR/localControllers')}">local controllers</a>.
                        </p>

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-plug"></i> Connected now</h2>

                        ${server.connected.length === 0
                              ? html`<p class="muted" id="ven-connected">No local controller is connected.</p>`
                              : html`<ul id="ven-connected">${repeat(server.connected, login => login, login => html`<li><code>${login}</code></li>`)}</ul>`}

                    </section>

                </div>
            `);

        }


        async function load(): Promise<void> {

            try
            {

                const server = await api.get();

                if (!cancelled)
                    draw(server);

            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">${errorMessage(problem)}</div>`);
            }

        }

        void load();

        return () => { cancelled = true; };

    }

};
