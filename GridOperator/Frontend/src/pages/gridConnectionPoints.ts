import { api, type GridConnectionPoints } from '../api/client';
import { auth } from '../auth';
import { pointFields, pointFrom } from '../grid/pointForm';
import { firstText, kW } from '../grid/words';
import { toURL } from '@node/basePath';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, repeat } from '@node/view';

/**
 * Where charging pools are connected to the grid of this grid operator: its
 * grid connection points, as POI 3 has them - the voltage level, the kind of
 * connection, the power contracted - each with the local controllers of the
 * pools behind it. A row opens the point; below the table, a new one is added.
 */
export const gridConnectionPointsPage: Page = {

    title: 'Grid connection points',

    render({ root, navigate }) {

        const content = shell(root, {
            active:    '/gridConnectionPoints',
            title:     'Grid connection points',
            subtitle:  'Where charging pools are connected to this grid.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange = auth.can('poi', 'edit');

        let cancelled  = false;
        let current:   GridConnectionPoints | null = null;

        const pathOf = (id: string) => `/gridConnectionPoints/${encodeURIComponent(id)}`;


        function draw(): void {

            if (current === null)
                return;

            const all = current;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at the grid connection points', 'add or change one')}</div>
                `}

                <div class="cards">

                    <section class="card wide">

                        <h2><i class="fa-solid fa-plug"></i> ${all.gridConnectionPoints.length} grid connection point(s) of ${all.gridOperatorId}</h2>

                        ${all.gridConnectionPoints.length === 0 ? html`
                            <p class="muted">None yet: add the first one below.</p>
                        ` : html`
                            <div class="table-scroll"><table class="records grid-table">
                                <thead>
                                    <tr>
                                        <th>Identification</th>
                                        <th>Name</th>
                                        <th>Voltage level</th>
                                        <th>Connection</th>
                                        <th class="number">Import</th>
                                        <th class="number">Export</th>
                                        <th>Local controllers</th>
                                        <th class="number">Limits</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${repeat(all.gridConnectionPoints, point => point['@id'], point => html`
                                        <tr class="clickable" @click=${() => navigate(pathOf(point['@id']))}>
                                            <td><a class="mono" href="${toURL(pathOf(point['@id']))}" @click=${(event: Event) => event.stopPropagation()}>${point['@id']}</a></td>
                                            <td>${firstText(point.name, '-')}</td>
                                            <td>${point.voltageLevel ?? '-'}</td>
                                            <td>${point.connectionType ?? '-'}</td>
                                            <td class="number">${kW(point.contractedImportPower)}</td>
                                            <td class="number">${kW(point.contractedExportPower)}</td>
                                            <td>${point.localControllers.length > 0 ? point.localControllers.join(', ') : html`<span class="muted">none</span>`}</td>
                                            <td class="number">${point.limits.length}</td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table></div>
                        `}

                    </section>

                    ${mayChange ? html`
                        <section class="card wide">

                            <h2><i class="fa-solid fa-plus"></i> New grid connection point</h2>

                            <form id="new-point-form" class="form-stack" @submit=${add}>

                                ${pointFields({ point: null, voltageLevels: all.voltageLevels, connectionTypes: all.connectionTypes, disabled: false })}

                                <div class="form-actions">
                                    <button type="submit" class="btn primary">Add</button>
                                    <span id="new-point-note"  class="form-notice" role="status"></span>
                                    <span id="new-point-error" class="form-error"  role="alert"></span>
                                </div>

                                <span class="hint">
                                    The identification is the grid operator's, unique in the whole roaming network, compared
                                    without case. Powers in kW; an empty field is unknown. Active power may not exceed the
                                    apparent power.
                                </span>

                            </form>

                        </section>
                    ` : nothing}

                </div>

            `);

        }


        async function add(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form  = event.currentTarget as HTMLFormElement;
            const note  = must<HTMLElement>(content, '#new-point-note');
            const json  = pointFrom(form, null);

            note.textContent = '';
            must<HTMLElement>(content, '#new-point-error').textContent = '';

            try
            {

                const added = await whileSaving(content, note, () => api.gridConnectionPoints.add(json));

                if (cancelled)
                    return;

                form.reset();
                navigate(pathOf(added['@id']));

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#new-point-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {
                const loaded = await api.gridConnectionPoints.list();

                if (!cancelled) {
                    current = loaded;
                    draw();
                }
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The grid connection points could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#new-point-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
