import { api, type GridConnectionPoint, type GridConnectionPoints } from '../api/client';
import { auth } from '../auth';
import { pointFields, pointFrom } from '../grid/pointForm';
import { dayAndTime, firstText, inKW, kW, kindWord, stateWord } from '../grid/words';
import { toURL } from '@node/basePath';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, repeat } from '@node/view';

/**
 * One grid connection point: what it is, the local controllers of the pools
 * behind it and the limits for them that are not over - and, for whoever may,
 * its fields to change and a way to remove it, which the grid operator
 * refuses while a local controller is still behind it.
 */
export const gridConnectionPointPage: Page = {

    title: 'Grid connection point',

    render({ root, params, navigate }) {

        const id = params['id'] ?? '';

        const content = shell(root, {
            active:    '/gridConnectionPoints',
            title:     `Grid connection point ${id}`,
            subtitle:  'Where charging pools are connected to this grid, and who controls them.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange = auth.can('poi', 'edit');

        let cancelled  = false;
        let point:     GridConnectionPoint  | null = null;
        let lists:     GridConnectionPoints | null = null;


        function draw(): void {

            if (point === null || lists === null)
                return;

            const it = point;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at this grid connection point', 'change it')}</div>
                `}

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-plug"></i> ${firstText(it.name, it['@id'])}</h2>

                        <div class="kv"><span class="k">Identification</span>  <span class="v mono">${it['@id']}</span></div>
                        <div class="kv"><span class="k">Voltage level</span>   <span class="v">${it.voltageLevel ?? '-'}</span></div>
                        <div class="kv"><span class="k">Connection</span>      <span class="v">${it.connectionType ?? '-'}</span></div>
                        <div class="kv"><span class="k">Import</span>          <span class="v">${kW(it.contractedImportPower)}</span></div>
                        <div class="kv"><span class="k">Export</span>          <span class="v">${kW(it.contractedExportPower)}</span></div>
                        <div class="kv"><span class="k">Market locations</span><span class="v mono">${(it.marketLocationIds ?? []).join(', ') || '-'}</span></div>

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-charging-station"></i> Behind it</h2>

                        ${it.localControllers.length === 0 ? html`
                            <p class="muted">No local controller is behind this point yet.</p>
                        ` : html`
                            <ul>
                                ${it.localControllers.map(name => html`<li class="mono">${name}</li>`)}
                            </ul>
                        `}

                        <p class="small"><a href="${toURL('/openADR/localControllers')}">Local controllers</a> · <a href="${toURL('/openADR/limits')}">Limits</a></p>

                        ${it.limits.length > 0 ? html`
                            <h3>Limits not over</h3>
                            <div class="table-scroll"><table class="records grid-table">
                                <thead><tr><th>State</th><th class="number">Limit</th><th>From</th><th>To</th></tr></thead>
                                <tbody>
                                    ${repeat(it.limits, limit => limit.id, limit => html`
                                        <tr>
                                            <td><span class="limit-state state-${limit.state}">${stateWord(limit.state)}</span></td>
                                            <td class="number">${inKW(limit.limitKW)} <span class="muted small">${kindWord(limit.kind)}</span></td>
                                            <td>${dayAndTime(limit.start)}</td>
                                            <td>${dayAndTime(limit.end)}</td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table></div>
                        ` : nothing}

                    </section>

                    <section class="card wide">

                        <h2><i class="fa-solid fa-pen"></i> Change</h2>

                        <form id="point-form" class="form-stack" @submit=${save}>

                            ${pointFields({ point: it, voltageLevels: lists.voltageLevels, connectionTypes: lists.connectionTypes, disabled: !mayChange })}

                            <div class="form-actions">
                                <button type="submit" class="btn primary" ?disabled=${!mayChange}>Save</button>
                                <button type="button" class="btn danger" ?disabled=${!mayChange || it.localControllers.length > 0} @click=${remove}>Remove</button>
                                <span id="point-note"  class="form-notice" role="status"></span>
                                <span id="point-error" class="form-error"  role="alert"></span>
                            </div>

                            ${it.localControllers.length > 0 ? html`
                                <span class="hint">It can not be removed while a local controller is behind it.</span>
                            ` : nothing}

                        </form>

                    </section>

                </div>

            `);

        }


        async function save(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            if (point === null)
                return;

            const form  = event.currentTarget as HTMLFormElement;
            const note  = must<HTMLElement>(content, '#point-note');
            const json  = pointFrom(form, point);

            note.textContent = '';
            must<HTMLElement>(content, '#point-error').textContent = '';

            try
            {

                point = await whileSaving(content, note, () => api.gridConnectionPoints.save(id, json));

                if (cancelled)
                    return;

                draw();
                form.reset();

                note.textContent = 'Saved.';

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#point-error').textContent = errorMessage(problem);
            }

        }


        async function remove(): Promise<void> {

            if (!confirm(`Remove the grid connection point ${id}? A roaming network it was published to loses it at the next publication.`))
                return;

            const note = must<HTMLElement>(content, '#point-note');

            try
            {
                await whileSaving(content, note, () => api.gridConnectionPoints.remove(id));

                if (!cancelled)
                    navigate('/gridConnectionPoints');
            }
            catch (problem)
            {
                must<HTMLElement>(content, '#point-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {

                const [ loaded, all ] = await Promise.all([ api.gridConnectionPoints.get(id), api.gridConnectionPoints.list() ]);

                if (!cancelled) {
                    point  = loaded;
                    lists  = all;
                    draw();
                }

            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`
                        <div class="error-box">The grid connection point ${id} could not be loaded: ${errorMessage(problem)}</div>
                        <p><a href="${toURL('/gridConnectionPoints')}">All grid connection points</a></p>
                    `);
            }

        }

        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#point-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
