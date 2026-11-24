import { api, type GridConnectionPoints, type LocalController, type LocalControllers, type NewVENConnection, type VENConnection, type VENConnectionMade } from '../api/client';
import { auth } from '../auth';
import { dayAndTime } from '../grid/words';
import { toURL } from '@node/basePath';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { copyText, errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, repeat } from '@node/view';

/**
 * The local controllers of the charging pools on this grid, as the VENs of
 * OpenADR 3 that may sign in at its VTN: each with the pool it controls and
 * the grid connection point that pool is connected to, when it last asked
 * anything, and the limits meant for it - and below it the connections its
 * VEN connects over, with their binding and where they stand, and its
 * subscriptions, each told over a connection or to a webhook.
 *
 * Whoever may adds one; its secret is made then and said once, in a box above
 * the list, together with where it signs in - and kept nowhere but as a hash.
 * A secret lost is a new one made, which the old one stops working with.
 *
 * Whoever may lets one connect, too - over the WebSocket port of OpenADR or
 * over SSH, in messages or REST in a tunnel: a connection and a login are
 * written down for it, and what it connects with is said once in the same way,
 * the password made for it or, over SSH, the key it brought. A connection is
 * taken away below the local controller it is of, and its login with it.
 */
export const localControllersPage: Page = {

    title: 'Local controllers',

    render({ root }) {

        const content = shell(root, {
            active:    '/openADR/localControllers',
            title:     'Local controllers',
            subtitle:  'The VENs of the charging pools on this grid, and how they sign in.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange = auth.can('openadr', 'edit');

        let cancelled  = false;
        let current:   LocalControllers     | null = null;
        let points:    GridConnectionPoints | null = null;

        /** The local controller whose secret was just made, said once - until the page is left or reloaded. */
        let justMade:  LocalController | null = null;

        /** The connection just made, with its password said once - until the page is left or reloaded. */
        let justConnected: VENConnectionMade | null = null;


        function draw(): void {

            if (current === null)
                return;

            const all          = current;
            const pointIds     = (points?.gridConnectionPoints ?? []).map(point => point['@id']);
            // A grid operator that does not say: both offered, and it says no where it must.
            const connectable  = all.connectable ?? { ws: true, ssh: true };
            const made         = justConnected;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at the local controllers', 'let one sign in or change how')}</div>
                `}

                ${justMade?.credentials ? html`
                    <div class="secret-box" role="status">
                        <strong>What ${justMade.venName} signs in with - its secret is said this once:</strong>
                        <div class="kv"><span class="k">Token URL</span>      <span class="v mono">${justMade.credentials.tokenURL}</span></div>
                        <div class="kv"><span class="k">VTN</span>            <span class="v mono">${justMade.credentials.vtnURL}</span></div>
                        <div class="kv"><span class="k">Client ID</span>      <span class="v mono">${justMade.credentials.clientID}</span></div>
                        <div class="kv"><span class="k">Client secret</span>  <span class="v mono">${justMade.credentials.clientSecret}</span></div>
                        <div class="form-actions">
                            <button type="button" class="btn small" @click=${copySecret}>Copy the secret</button>
                            <span id="secret-note" class="form-notice" role="status"></span>
                        </div>
                    </div>
                ` : nothing}

                ${made ? html`
                    <div class="secret-box connection-box" role="status">
                        <strong>What ${made.venName} connects with - ${made.credentials.password ? 'its password is said this once' : 'it signs in with its SSH key'}:</strong>
                        <div class="kv"><span class="k">URL</span>            <span class="v mono">${made.credentials.vtnURL}</span></div>
                        <div class="kv"><span class="k">Binding</span>        <span class="v mono">${made.credentials.openADRBinding}</span></div>
                        <div class="kv"><span class="k">Login</span>          <span class="v mono">${made.credentials.login}</span></div>
                        ${made.credentials.password ? html`
                            <div class="kv"><span class="k">Password</span>   <span class="v mono">${made.credentials.password}</span></div>
                        ` : nothing}
                        ${made.credentials.sshHostKey ? html`
                            <div class="kv"><span class="k">SSH host key</span> <span class="v mono">${made.credentials.sshHostKey}</span></div>
                        ` : nothing}
                        <span class="hint">
                            At the local controller the URL is its VTN URL, and the URL of its connection to this grid operator,
                            of the type GridOperator, in this binding${made.credentials.sshHostKey ? ', pinning this host key' : ''}.
                        </span>
                        ${made.credentials.password ? html`
                            <div class="form-actions">
                                <button type="button" class="btn small" @click=${copyPassword}>Copy the password</button>
                                <span id="password-note" class="form-notice" role="status"></span>
                            </div>
                        ` : nothing}
                    </div>
                ` : nothing}

                <div class="cards">

                    <section class="card wide">

                        <h2><i class="fa-solid fa-charging-station"></i> ${all.localControllers.length} local controller(s)</h2>

                        ${all.localControllers.length === 0 ? html`
                            <p class="muted">No local controller may sign in yet.</p>
                        ` : html`
                            <div class="table-scroll"><table class="records grid-table">
                                <thead>
                                    <tr>
                                        <th>VEN name</th>
                                        <th>Charging pool</th>
                                        <th>Grid connection point</th>
                                        <th>Last seen</th>
                                        <th class="number">Limits</th>
                                        <th class="number">Reports</th>
                                        <th></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${repeat(all.localControllers, controller => controller.venName, controller => html`
                                        <tr class="local-controller">
                                            <td><span class="mono">${controller.venName}</span>${controller.description ? html`<div class="small muted">${controller.description}</div>` : nothing}</td>
                                            <td class="mono">${controller.chargingPoolId}</td>
                                            <td><a class="mono" href="${toURL(`/gridConnectionPoints/${encodeURIComponent(controller.gridConnectionPointId)}`)}">${controller.gridConnectionPointId}</a></td>
                                            <td>${controller.lastSeen ? dayAndTime(controller.lastSeen) : html`<span class="muted">not since this start</span>`}</td>
                                            <td class="number">${controller.limits.length}</td>
                                            <td class="number">${controller.reports}</td>
                                            <td class="actions">
                                                <button type="button" class="btn small"        ?disabled=${!mayChange} @click=${() => renew(controller)}>New secret</button>
                                                <button type="button" class="btn small danger" ?disabled=${!mayChange} @click=${() => remove(controller)}>Remove</button>
                                            </td>
                                        </tr>
                                        <tr class="ven-links">
                                            <td colspan="7">
                                                <div class="ven-links-grid">
                                                    ${connectionsOf(controller)}
                                                    ${subscriptionsOf(controller)}
                                                </div>
                                            </td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table></div>
                        `}

                        <span id="list-error" class="form-error" role="alert"></span>

                        <span class="hint">
                            They sign in at ${all.tokenURL} with the client credentials of OAuth 2.0 - their VEN name and
                            their secret - and read their limits from ${all.vtnURL}events.
                        </span>

                    </section>

                    ${mayChange ? html`
                        <section class="card wide">

                            <h2><i class="fa-solid fa-plus"></i> New local controller</h2>

                            ${pointIds.length === 0 ? html`
                                <p class="muted">
                                    A local controller is behind a grid connection point: add the
                                    <a href="${toURL('/gridConnectionPoints')}">grid connection point</a> its pool is connected to first.
                                </p>
                            ` : html`
                                <form id="new-local-controller-form" class="form-stack" @submit=${add}>

                                    <div class="form-grid">

                                        <label>VEN name
                                            <input type="text" name="venName" required maxlength="64" pattern="[A-Za-z0-9._:\\-]{1,64}" placeholder="lc-paradies" />
                                        </label>

                                        <label>Charging pool
                                            <input type="text" name="chargingPoolId" required placeholder="DE*GEF*P1" />
                                        </label>

                                        <label>Grid connection point
                                            <select name="gridConnectionPointId">
                                                ${pointIds.map(id => html`<option value="${id}">${id}</option>`)}
                                            </select>
                                        </label>

                                        <label>Description
                                            <input type="text" name="description" maxlength="200" />
                                        </label>

                                    </div>

                                    <div class="form-actions">
                                        <button type="submit" class="btn primary">Add</button>
                                        <span id="new-local-controller-note"  class="form-notice" role="status"></span>
                                        <span id="new-local-controller-error" class="form-error"  role="alert"></span>
                                    </div>

                                    <span class="hint">
                                        The VEN name is its client ID as well: letters, digits, '.', '_', ':' and '-'. The
                                        charging pool as POI names it, e.g. DE*GEF*P1.
                                    </span>

                                </form>
                            `}

                        </section>
                    ` : nothing}

                    ${mayChange && all.localControllers.length > 0 ? html`
                        <section class="card wide">

                            <h2><i class="fa-solid fa-plug"></i> Let a local controller connect</h2>

                            ${!connectable.ws && !connectable.ssh ? html`
                                <p class="muted">
                                    Neither the WebSocket port of OpenADR nor OpenADR over SSH is switched on:
                                    <span class="mono">webSocketServer.enabled</span> - and its <span class="mono">ssh.enabled</span>,
                                    with the SSH server of this grid operator - in the configuration switch them on.
                                </p>
                            ` : html`
                                <form id="new-connection-form" class="form-stack" @submit=${connect}>

                                    <div class="form-grid">

                                        <label>Local controller
                                            <select name="venName">
                                                ${all.localControllers.map(controller => html`<option value="${controller.venName}">${controller.venName}</option>`)}
                                            </select>
                                        </label>

                                        <label>Over
                                            <select name="transport">
                                                <option value="ws"  ?disabled=${!connectable.ws}  ?selected=${connectable.ws}>the WebSocket port${connectable.ws  ? '' : ' - switched off'}</option>
                                                <option value="ssh" ?disabled=${!connectable.ssh} ?selected=${!connectable.ws}>SSH${connectable.ssh ? '' : ' - switched off'}</option>
                                            </select>
                                        </label>

                                        <label>Binding
                                            <select name="binding">
                                                <option value="messages" selected>messages</option>
                                                <option value="rest">REST in a tunnel</option>
                                            </select>
                                        </label>

                                        <label>Description
                                            <input type="text" name="description" maxlength="200" />
                                        </label>

                                    </div>

                                    <label>SSH key
                                        <textarea name="sshKey" rows="2" class="mono" placeholder="ssh-ed25519 AAAA... lc-paradies"></textarea>
                                    </label>

                                    <div class="form-actions">
                                        <button type="submit" class="btn primary">Connect</button>
                                        <span id="new-connection-note"  class="form-notice" role="status"></span>
                                        <span id="new-connection-error" class="form-error"  role="alert"></span>
                                    </div>

                                    <span class="hint">
                                        A password is made for it and said once - or, over SSH, it signs in with the key given here,
                                        one line of an authorized_keys file. It comes under its VEN name, or its VEN name and the way
                                        where that is taken. Messages are the JSON messages of OpenADR; REST in a tunnel is the VTN's
                                        REST API through the connection.
                                    </span>

                                </form>
                            `}

                        </section>
                    ` : nothing}

                </div>

            `);

        }


        /**
         * The connections the VEN of a local controller connects over: over
         * what, in which binding, switched on or off, whether it is in - or
         * why not -, and, in the binding of messages, how many notifications
         * wait to be told over it.
         */
        function connectionsOf(Controller: LocalController) {

            return html`
                <div class="ven-connections">
                    <div class="small muted">Connections</div>
                    ${Controller.connections.length === 0 ? html`
                        <span class="small muted">None: it reaches the VTN over HTTPS only.</span>
                    ` : html`
                        <ul class="ven-list">
                            ${Controller.connections.map(connection => html`
                                <li class="ven-connection" data-id="${connection.id}">
                                    <span class="chips">
                                        <span class="chip">${connection.transport}</span>
                                        <span class="chip">${bindingWord(connection.binding)}</span>
                                        <span class="chip ${connection.enabled ? 'on' : ''}">${connection.enabled ? 'on' : 'off'}</span>
                                        <span class="chip ${statusClass(connection)}" title="${connection.said ?? ''}">${statusWord(connection)}</span>
                                        ${connection.binding === 'messages' ? html`
                                            <span class="chip waiting ${connection.notificationsWaiting > 0 ? 'warn' : ''}">${connection.notificationsWaiting} notification(s) waiting</span>
                                        ` : nothing}
                                    </span>
                                    <span class="mono">${connection.login ?? connection.id}</span>
                                    ${mayChange ? html`
                                        <button type="button" class="btn small danger" @click=${() => disconnect(Controller, connection)}>Take away</button>
                                    ` : nothing}
                                    ${connection.said ? html`<div class="small muted">${connection.said}</div>` : nothing}
                                </li>
                            `)}
                        </ul>
                    `}
                </div>
            `;

        }


        /**
         * The subscriptions of a local controller: what each asks to be told
         * of, and where - over the connection it was made over, or to a webhook.
         */
        function subscriptionsOf(Controller: LocalController) {

            return html`
                <div class="ven-subscriptions">
                    <div class="small muted">Subscriptions</div>
                    ${Controller.subscriptions.length === 0 ? html`
                        <span class="small muted">None.</span>
                    ` : html`
                        <ul class="ven-list">
                            ${Controller.subscriptions.map(subscription => html`
                                <li class="ven-subscription">
                                    ${subscription.objectOperations.map(objectOperation => html`
                                        <div>
                                            <span class="chips">
                                                ${objectOperation.operations.map(operation => html`<span class="chip">${operation}</span>`)}
                                                ${objectOperation.objects.   map(object    => html`<span class="chip on">${object}</span>`)}
                                            </span>
                                            ${objectOperation.callback === 'connection'
                                                  ? html`over ${toldOverWord(Controller, subscription.toldOver)}`
                                                  : html`to the webhook`}
                                            <span class="mono small">${objectOperation.callbackUrl}</span>
                                        </div>
                                    `)}
                                    <div class="small muted">${subscription.clientName}${subscription.created ? `, since ${dayAndTime(subscription.created)}` : ''}</div>
                                </li>
                            `)}
                        </ul>
                    `}
                </div>
            `;

        }


        async function copySecret(): Promise<void> {
            if (justMade?.credentials)
                must<HTMLElement>(content, '#secret-note').textContent = await copyText(justMade.credentials.clientSecret);
        }


        async function copyPassword(): Promise<void> {
            if (justConnected?.credentials.password)
                must<HTMLElement>(content, '#password-note').textContent = await copyText(justConnected.credentials.password);
        }


        async function connect(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form         = event.currentTarget as HTMLFormElement;
            const note         = must<HTMLElement>(content, '#new-connection-note');

            const venName      = field(form, 'venName');
            const transport    = field(form, 'transport') as NewVENConnection['transport'];
            const binding      = field(form, 'binding')   as NewVENConnection['binding'];
            const sshKey       = field(form, 'sshKey');
            const description  = field(form, 'description');

            note.textContent = '';
            must<HTMLElement>(content, '#new-connection-error').textContent = '';

            try
            {

                const made = await whileSaving(content, note, () => api.openADR.connect(venName, {
                                                                     transport,
                                                                     binding,
                                                                     ...(sshKey.     length > 0 ? { sshKey }      : {}),
                                                                     ...(description.length > 0 ? { description } : {})
                                                                 }));

                if (cancelled)
                    return;

                justConnected = made;

                await load();
                form.reset();

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#new-connection-error').textContent = errorMessage(problem);
            }

        }


        async function disconnect(Controller: LocalController, Connection: VENConnection): Promise<void> {

            if (!confirm(`Take the connection ${Connection.login ?? Connection.id} of ${Controller.venName} away? Whatever is in over it is let go of, and the login it came under is removed with it.`))
                return;

            try
            {

                current = await api.openADR.disconnect(Controller.venName, Connection.id);

                if (justConnected?.id === Connection.id)
                    justConnected = null;

                if (!cancelled)
                    draw();

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#list-error').textContent = errorMessage(problem);
            }

        }


        async function add(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form          = event.currentTarget as HTMLFormElement;
            const note          = must<HTMLElement>(content, '#new-local-controller-note');

            const venName       = field(form, 'venName');
            const poolId        = field(form, 'chargingPoolId');
            const pointId       = field(form, 'gridConnectionPointId');
            const description   = field(form, 'description');

            note.textContent = '';
            must<HTMLElement>(content, '#new-local-controller-error').textContent = '';

            try
            {

                const added = await whileSaving(content, note, () => api.openADR.addLocalController({
                                                                      venName,
                                                                      chargingPoolId:         poolId,
                                                                      gridConnectionPointId:  pointId,
                                                                      ...(description.length > 0 ? { description } : {})
                                                                  }));

                if (cancelled)
                    return;

                justMade = added;

                await load();
                form.reset();

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#new-local-controller-error').textContent = errorMessage(problem);
            }

        }


        async function renew(Controller: LocalController): Promise<void> {

            if (!confirm(`A new secret for ${Controller.venName}? The old one signs in no more from now on, and neither does a token handed out for it.`))
                return;

            try
            {
                justMade = await api.openADR.newSecret(Controller.venName);

                if (!cancelled)
                    await load();
            }
            catch (problem)
            {
                must<HTMLElement>(content, '#list-error').textContent = errorMessage(problem);
            }

        }


        async function remove(Controller: LocalController): Promise<void> {

            if (!confirm(`Remove ${Controller.venName}? It signs in no more, and reads no limit any more.`))
                return;

            try
            {

                current = await api.openADR.removeLocalController(Controller.venName);

                if (justMade?.venName === Controller.venName)
                    justMade = null;

                if (justConnected?.venName === Controller.venName)
                    justConnected = null;

                if (!cancelled)
                    draw();

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#list-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {

                const [ loaded, all ] = await Promise.all([
                                            api.openADR.localControllers(),
                                            auth.can('poi', 'read') ? api.gridConnectionPoints.list() : Promise.resolve(null)
                                        ]);

                if (!cancelled) {
                    current  = loaded;
                    points   = all;
                    draw();
                }

            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The local controllers could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        async function reload(): Promise<void> {

            justMade       = null;
            justConnected  = null;

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#new-local-controller-form')) ||
                                             typedSinceDrawn(content.querySelector('#new-connection-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};


/** How a binding of OpenADR is said: its REST API in a tunnel, or its JSON messages. */
function bindingWord(Binding: VENConnection['binding']): string {
    return Binding === 'rest' ? 'REST in a tunnel' : 'messages';
}

/** Where a connection stands, in a word: in now, or what was said of it last. */
function statusWord(Connection: VENConnection): string {
    return Connection.in ? 'in now' : Connection.status ?? 'nothing said yet';
}

/** The colour of where a connection stands: in, refused, or anything else. */
function statusClass(Connection: VENConnection): string {
    if (Connection.in)
        return 'ok';
    return Connection.status === 'refused' || Connection.status === 'failed' ? 'bad' : 'warn';
}

/** The connection a subscription is told over: by its login, where it is one of the local controller's - or none. */
function toldOverWord(Controller: LocalController, ConnectionId: string | null): string {
    if (ConnectionId === null)
        return 'no connection now';
    const connection = Controller.connections.find(one => one.id === ConnectionId);
    return `the connection ${connection?.login ?? ConnectionId}`;
}
