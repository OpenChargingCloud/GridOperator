import { api, type GridConnectionPoints, type LocalController, type LocalControllers } from '../api/client';
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
 * anything, and the limits meant for it.
 *
 * Whoever may adds one; its secret is made then and said once, in a box above
 * the list, together with where it signs in - and kept nowhere but as a hash.
 * A secret lost is a new one made, which the old one stops working with.
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


        function draw(): void {

            if (current === null)
                return;

            const all       = current;
            const pointIds  = (points?.gridConnectionPoints ?? []).map(point => point['@id']);

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
                                        <tr>
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

                </div>

            `);

        }


        async function copySecret(): Promise<void> {
            if (justMade?.credentials)
                must<HTMLElement>(content, '#secret-note').textContent = await copyText(justMade.credentials.clientSecret);
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

            justMade = null;

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#new-local-controller-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
