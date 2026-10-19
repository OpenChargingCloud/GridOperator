import { api, type GridConnectionPoints, type Limit, type Limits, type OpenADRInfo, type Reports } from '../api/client';
import { auth } from '../auth';
import { dayAndTime, inKW, kindWord, stateWord } from '../grid/words';
import { toURL } from '@node/basePath';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, numberField, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, repeat } from '@node/view';

/**
 * The limits this grid operator sends to the local controllers over OpenADR
 * 3: how much a charging pool may take from the grid, or feed into it, from
 * when and for how long - for the local controllers behind one grid
 * connection point, or for all of them. Each is an event of the program
 * "grid-capacity", read by the local controllers it is for and by nobody else;
 * cancelled, they read it no more.
 *
 * Below, what the local controllers reported back.
 *
 * Asked again every ten seconds, so that a limit moves from scheduled to
 * active to over while somebody looks.
 */
export const limitsPage: Page = {

    title: 'Limits',

    render({ root }) {

        const content = shell(root, {
            active:    '/openADR/limits',
            title:     'Limits',
            subtitle:  'What the local controllers on this grid may take from it, and feed into it.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const maySend = auth.can('openadr', 'run');

        let cancelled  = false;
        let timer:     ReturnType<typeof setTimeout> | undefined;

        let vtn:       OpenADRInfo          | null = null;
        let limits:    Limits               | null = null;
        let reports:   Reports              | null = null;
        let points:    GridConnectionPoints | null = null;


        function draw(): void {

            if (vtn === null || limits === null)
                return;

            const info      = vtn;
            const all       = limits.limits;
            const pointIds  = (points?.gridConnectionPoints ?? []).
                                  filter(point => point.localControllers.length > 0).
                                  map   (point => point['@id']);

            render(content, html`

                ${maySend ? nothing : html`
                    <div class="notice">${mayButNot('look at the limits', 'send or cancel one')}</div>
                `}

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-tower-broadcast"></i> OpenADR 3 VTN</h2>

                        <div class="kv"><span class="k">VTN</span>                <span class="v mono">${info.vtnURL}</span></div>
                        <div class="kv"><span class="k">Token</span>              <span class="v mono">${info.tokenURL}</span></div>
                        <div class="kv"><span class="k">Program</span>            <span class="v mono">${String(info.program['programName'] ?? '')}</span></div>
                        <div class="kv"><span class="k">Local controllers</span>  <span class="v">${info.localControllers} <span class="muted">(${info.signedIn} token(s) signing in)</span></span></div>
                        <div class="kv"><span class="k">Limits</span>             <span class="v">${info.activeLimits} active, ${info.scheduledLimits} scheduled</span></div>

                        <p class="small"><a href="${toURL('/openADR/localControllers')}">Local controllers</a></p>

                    </section>

                    ${maySend ? html`
                        <section class="card">

                            <h2><i class="fa-solid fa-gauge-high"></i> Send a limit</h2>

                            ${info.localControllers === 0 ? html`
                                <p class="muted">There is no local controller to send a limit to yet.</p>
                            ` : html`
                                <form id="new-limit-form" class="form-stack" @submit=${send}>

                                    <div class="form-grid">

                                        <label>For
                                            <select name="gridConnectionPointId">
                                                <option value="">every local controller</option>
                                                ${pointIds.map(id => html`<option value="${id}">behind ${id}</option>`)}
                                            </select>
                                        </label>

                                        <label>Kind
                                            <select name="kind">
                                                ${info.kinds.map(kind => html`<option value="${kind}">${kindWord(kind)}</option>`)}
                                            </select>
                                        </label>

                                        <label>Limit (kW)
                                            <input type="number" name="limitKW" required min="0" step="any" placeholder="44" />
                                        </label>

                                        <label>Duration (minutes)
                                            <input type="number" name="durationMinutes" required min="1" max="${info.maxDurationMinutes}" step="1" value="60" />
                                        </label>

                                        <label>From
                                            <input type="datetime-local" name="start" />
                                        </label>

                                        <label>Name
                                            <input type="text" name="name" maxlength="200" placeholder="Evening peak" />
                                        </label>

                                    </div>

                                    <div class="form-actions">
                                        <button type="submit" class="btn primary">Send</button>
                                        <span id="new-limit-note"  class="form-notice" role="status"></span>
                                        <span id="new-limit-error" class="form-error"  role="alert"></span>
                                    </div>

                                    <span class="hint">From now, where "From" is empty. In ${info.unit}; 0 means nothing at all.</span>

                                </form>
                            `}

                        </section>
                    ` : nothing}

                    <section class="card wide">

                        <h2><i class="fa-solid fa-list"></i> ${all.length} limit(s)</h2>

                        ${all.length === 0 ? html`
                            <p class="muted">No limit was sent yet.</p>
                        ` : html`
                            <div class="table-scroll"><table class="records grid-table">
                                <thead>
                                    <tr>
                                        <th>State</th>
                                        <th class="number">Limit</th>
                                        <th>From</th>
                                        <th>To</th>
                                        <th>For</th>
                                        <th>Name</th>
                                        <th>Sent by</th>
                                        <th></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${repeat(all, limit => limit.id, limit => html`
                                        <tr>
                                            <td><span class="limit-state state-${limit.state}">${stateWord(limit.state)}</span></td>
                                            <td class="number">${inKW(limit.limitKW)}<div class="small muted">${kindWord(limit.kind)}</div></td>
                                            <td>${dayAndTime(limit.start)}</td>
                                            <td>${dayAndTime(limit.end)}</td>
                                            <td>${limit.gridConnectionPointId ? html`behind <span class="mono">${limit.gridConnectionPointId}</span>` : 'every one'}
                                                <div class="small muted">${limit.localControllers.join(', ')}</div></td>
                                            <td>${limit.name ?? html`<span class="muted">-</span>`}</td>
                                            <td>${limit.sentBy}</td>
                                            <td class="actions">
                                                ${limit.state !== 'over' ? html`
                                                    <button type="button" class="btn small danger" ?disabled=${!maySend} @click=${() => cancel(limit)}>Cancel</button>
                                                ` : nothing}
                                            </td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table></div>
                        `}

                        <span id="limits-error" class="form-error" role="alert"></span>

                    </section>

                    <section class="card wide">

                        <h2><i class="fa-solid fa-clipboard-check"></i> Reports</h2>

                        ${(reports?.reports.length ?? 0) === 0 ? html`
                            <p class="muted">No local controller reported anything since this start.</p>
                        ` : html`
                            <div class="table-scroll"><table class="records grid-table">
                                <thead><tr><th>When</th><th>From</th><th>On the event</th><th>Report</th></tr></thead>
                                <tbody>
                                    ${repeat(reports!.reports, report => String(report['id']), report => html`
                                        <tr>
                                            <td>${dayAndTime(String(report['createdDateTime'] ?? ''))}</td>
                                            <td class="mono">${String(report['clientName'] ?? '')}</td>
                                            <td class="mono small">${String(report['eventID'] ?? '')}</td>
                                            <td>${String(report['reportName'] ?? '-')}</td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table></div>
                            <span class="hint">The newest ${reports!.capacity} are kept, in memory: a restart forgets them.</span>
                        `}

                    </section>

                </div>

            `);

        }


        async function send(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form      = event.currentTarget as HTMLFormElement;
            const note      = must<HTMLElement>(content, '#new-limit-note');

            const pointId   = field(form, 'gridConnectionPointId');
            const start     = field(form, 'start');
            const name      = field(form, 'name');
            const limitKW   = numberField(form, 'limitKW');
            const minutes   = numberField(form, 'durationMinutes');
            const kind      = field(form, 'kind');

            note.textContent = '';
            must<HTMLElement>(content, '#new-limit-error').textContent = '';

            try
            {

                await whileSaving(content, note, () => api.openADR.sendLimit({
                                                           kind,
                                                           limitKW,
                                                           durationMinutes:  minutes,
                                                           ...(pointId.length > 0 ? { gridConnectionPointId: pointId }                 : {}),
                                                           ...(start.length   > 0 ? { start: new Date(start).toISOString() }           : {}),
                                                           ...(name.length    > 0 ? { name }                                           : {})
                                                       }));

                if (cancelled)
                    return;

                form.reset();
                await load();

                must<HTMLElement>(content, '#new-limit-note').textContent = 'Sent.';

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#new-limit-error').textContent = errorMessage(problem);
            }

        }


        async function cancel(Limit: Limit): Promise<void> {

            if (!confirm(`Cancel the limit of ${inKW(Limit.limitKW)} for ${Limit.localControllers.join(', ')}? They read it no more.`))
                return;

            try
            {
                limits = await api.openADR.cancelLimit(Limit.id);

                if (!cancelled)
                    await load();
            }
            catch (problem)
            {
                must<HTMLElement>(content, '#limits-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            clearTimeout(timer);

            try
            {

                const [ info, all, reported, grid ] = await Promise.all([
                                                          api.openADR.get(),
                                                          api.openADR.limits(),
                                                          api.openADR.reports(),
                                                          auth.can('poi', 'read') ? api.gridConnectionPoints.list() : Promise.resolve(null)
                                                      ]);

                if (cancelled)
                    return;

                vtn      = info;
                limits   = all;
                reports  = reported;
                points   = grid;

                draw();

                timer = setTimeout(() => void load(), 10_000);

            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The limits could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#new-limit-form')));

        void load();

        return () => { cancelled = true; clearTimeout(timer); release(); };

    }

};
