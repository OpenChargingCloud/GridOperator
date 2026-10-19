import { api, type Publication, type RoamingNetworkSettings } from '../api/client';
import { auth } from '../auth';
import { toURL } from '@node/basePath';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render } from '@node/view';

/**
 * The roaming network this grid operator publishes itself to: its URL, as a
 * roaming hub serves it, kept in the configuration file - and what publishing
 * would change there now, asked of it, with the publication itself for
 * whoever may: one change set of its head, signed by this grid operator.
 *
 * A roaming network that has not added the grid operator yet says so; its
 * operator adds it with what Grid operator → Party has.
 */
export const roamingNetworkPage: Page = {

    title: 'Roaming network',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/roamingNetwork',
            title:     'Roaming network',
            subtitle:  'Where this grid operator and its grid connection points are published.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange   = auth.can('poi', 'edit');
        const mayPublish  = auth.can('poi', 'run');

        let cancelled    = false;
        let current:     RoamingNetworkSettings | null = null;
        let publication: Publication            | null = null;
        let problem:     string                 | null = null;


        function draw(): void {

            if (current === null)
                return;

            const settings = current;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at where this grid operator is published', 'change it')}</div>
                `}

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-share-nodes"></i> Roaming network</h2>

                        <form id="roaming-network-form" class="form-stack" @submit=${save}>

                            <label>URL
                                <input type="url" name="url" value="${settings.url}" maxlength="${settings.maxURLLength}"
                                       placeholder="http://127.0.0.1:2356/poi/RNs/QA" ?disabled=${!mayChange} />
                            </label>

                            <div class="form-actions">
                                <button type="submit" class="btn primary" ?disabled=${!mayChange}>Save</button>
                                <span id="roaming-network-note"  class="form-notice" role="status"></span>
                                <span id="roaming-network-error" class="form-error"  role="alert"></span>
                            </div>

                            <span class="hint">
                                The roaming network as its hub serves it, e.g. http://127.0.0.1:2356/poi/RNs/QA. Kept in
                                ${settings.file}. Empty: published nowhere.
                            </span>

                        </form>

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-upload"></i> Publication</h2>

                        ${settings.url.length === 0 ? html`
                            <p class="muted">No roaming network is set: there is nowhere to publish to.</p>
                        ` : html`

                            <div class="form-actions">
                                <button type="button" class="btn" @click=${preview}>What would change</button>
                                <button type="button" class="btn primary" ?disabled=${!mayPublish || publication?.isParty !== true || publication.changes === 0}
                                        @click=${publish}>Publish</button>
                                <span id="publication-note" class="form-notice" role="status"></span>
                            </div>

                            ${problem !== null ? html`<div class="error-box">${problem}</div>` : nothing}

                            ${publication !== null ? html`
                                <p>${publication.message}</p>
                                ${publication.isParty === false ? html`
                                    <p class="small">Its operator adds this grid operator with what <a href="${toURL('/gridOperator')}">Grid operator → Party</a> has.</p>
                                ` : nothing}
                                ${publication.steps.length > 0 ? html`
                                    <ul class="publication-steps">
                                        ${publication.steps.map(step => html`
                                            <li class="step-${step.kind}"><span class="step-kind">${step.kind}</span> ${step.text}</li>
                                        `)}
                                    </ul>
                                ` : nothing}
                            ` : html`
                                <p class="muted">Ask the roaming network what publishing would change; it signs nothing yet.</p>
                            `}

                            <span class="hint">
                                Published is one change set of the head of the roaming network, signed with the signing key of
                                ${settings.gridOperatorId}: what it says of itself, and the grid connection points it maintains.
                            </span>

                        `}

                    </section>

                </div>

            `);

        }


        async function save(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form  = event.currentTarget as HTMLFormElement;
            const note  = must<HTMLElement>(content, '#roaming-network-note');
            const url   = field(form, 'url');

            note.textContent = '';
            must<HTMLElement>(content, '#roaming-network-error').textContent = '';

            try
            {

                current      = await whileSaving(content, note, () => api.roamingNetwork.saveSettings({ url }));
                publication  = null;
                problem      = null;

                if (cancelled)
                    return;

                draw();
                form.reset();

                note.textContent = current.url.length > 0 ? 'Saved.' : 'Saved: published nowhere.';

            }
            catch (error)
            {
                must<HTMLElement>(content, '#roaming-network-error').textContent = errorMessage(error);
            }

        }


        async function ask(How: () => Promise<Publication>, Saying: string): Promise<void> {

            const note = must<HTMLElement>(content, '#publication-note');

            note.textContent = Saying;

            try
            {
                publication  = await How();
                problem      = null;
            }
            catch (error)
            {
                problem      = errorMessage(error);
            }

            if (!cancelled) {
                draw();
                must<HTMLElement>(content, '#publication-note').textContent = '';
            }

        }

        const preview = () => ask(() => api.roamingNetwork.preview(), 'Asking the roaming network ...');

        async function publish(): Promise<void> {

            if (!confirm(`Publish ${publication?.changes ?? 0} change(s) to ${current?.url}, signed by ${current?.gridOperatorId}?`))
                return;

            await ask(() => api.roamingNetwork.publish(), 'Publishing ...');

        }


        async function load(): Promise<void> {

            try
            {
                const loaded = await api.roamingNetwork.settings();

                if (!cancelled) {
                    current = loaded;
                    draw();
                }
            }
            catch (error)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The roaming network could not be loaded: ${errorMessage(error)}</div>`);
            }

        }

        async function reload(): Promise<void> {

            publication  = null;
            problem      = null;

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#roaming-network-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
