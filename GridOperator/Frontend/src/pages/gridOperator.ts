import { api, type GridOperatorChange, type GridOperatorInfo } from '../api/client';
import { auth } from '../auth';
import { dayAndTime, firstText, languageOf } from '../grid/words';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { copyText, errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render } from '@node/view';

/**
 * The grid operator this node is, as POI 3 knows it: who it is, the public
 * halves of its keys, and what it says of itself - its name, its address, its
 * homepage and its hotline, which change here. Its identification and its keys
 * do not: a roaming network knows it by them.
 *
 * And what the operator of a roaming network needs to add it as a party: its
 * identification, its name and its public keys, as one piece of JSON to hand
 * over - the private keys stay where they are.
 */
export const gridOperatorPage: Page = {

    title: 'Grid operator',

    render({ root }) {

        const content = shell(root, {
            active:    '/gridOperator',
            title:     'Grid operator',
            subtitle:  'Who this grid operator is in the points of interest, and what it says of itself.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange = auth.can('poi', 'edit');

        let cancelled  = false;
        let current:   GridOperatorInfo | null = null;


        function draw(): void {

            if (current === null)
                return;

            const info     = current;
            const entity   = info.gridOperator;
            const address  = entity.address ?? {};
            const party    = JSON.stringify(info.party, null, 2);

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at the grid operator', 'change what it says of itself')}</div>
                `}

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-building"></i> ${entity['@id']}</h2>

                        <div class="kv"><span class="k">Name</span>                     <span class="v">${firstText(entity.name, '-')}</span></div>
                        <div class="kv"><span class="k">Grid connection points</span>   <span class="v">${info.gridConnectionPoints}</span></div>
                        <div class="kv"><span class="k">Made</span>                     <span class="v">${dayAndTime(entity.created)}</span></div>
                        <div class="kv"><span class="k">Last change</span>              <span class="v">${dayAndTime(entity.lastChange)}</span></div>
                        <div class="kv"><span class="k">Signing key</span>              <span class="v mono small">${info.signingKey.id} <span class="muted">(${info.signingKey.curve})</span></span></div>
                        <div class="kv"><span class="k">Identity key</span>             <span class="v mono small">${info.identityKey.id} <span class="muted">(${info.identityKey.curve})</span></span></div>

                        <span class="hint">
                            Its private keys are kept in ${info.directory}, and leave it never: keep a copy of them where
                            nobody else can read it. The signing key signs every change set of this grid operator, the
                            identity key a change of its keys.
                        </span>

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-pen"></i> What it says of itself</h2>

                        <form id="grid-operator-form" class="form-stack" @submit=${save}>

                            <label>Name
                                <input type="text" name="name" value="${firstText(entity.name)}" maxlength="200" ?disabled=${!mayChange} />
                            </label>

                            <div class="form-grid">
                                <label>Street       <input type="text" name="street"      value="${address.street ?? ''}"      ?disabled=${!mayChange} /></label>
                                <label>House number <input type="text" name="houseNumber" value="${address.houseNumber ?? ''}" ?disabled=${!mayChange} /></label>
                                <label>Postal code  <input type="text" name="postalCode"  value="${address.postalCode ?? ''}"  ?disabled=${!mayChange} /></label>
                                <label>City         <input type="text" name="city"        value="${firstText(address.city)}"   ?disabled=${!mayChange} /></label>
                                <label>Country      <input type="text" name="country"     value="${address.country ?? ''}"     placeholder="DEU" maxlength="3" ?disabled=${!mayChange} /></label>
                            </div>

                            <label>Homepage
                                <input type="url" name="homepage" value="${entity.homepage ?? ''}" placeholder="https://netz.example.org" ?disabled=${!mayChange} />
                            </label>

                            <label>Hotline
                                <input type="tel" name="hotline" value="${entity.hotline ?? ''}" placeholder="+49 3641 7654321" ?disabled=${!mayChange} />
                            </label>

                            <div class="form-actions">
                                <button type="submit" class="btn primary" ?disabled=${!mayChange}>Save</button>
                                <span id="grid-operator-note"  class="form-notice" role="status"></span>
                                <span id="grid-operator-error" class="form-error"  role="alert"></span>
                            </div>

                            <span class="hint">
                                Kept here, and in a roaming network once it is published - Configuration → Roaming network.
                                An empty field is removed. The country is ISO 3166 alpha-3, e.g. DEU.
                            </span>

                        </form>

                    </section>

                    <section class="card wide">

                        <h2><i class="fa-solid fa-handshake"></i> Party</h2>

                        <p>
                            What the operator of a roaming network adds this grid operator with: its identification, its
                            name and the public halves of its keys. From then on it adds its grid connection points
                            itself, with change sets it signs.
                        </p>

                        <textarea class="json-box" readonly .value=${party}></textarea>

                        <div class="form-actions">
                            <button type="button" class="btn small" @click=${(event: Event) => copyParty(event, party)}>Copy</button>
                            <span id="party-note" class="form-notice" role="status"></span>
                        </div>

                    </section>

                </div>

            `);

        }


        async function copyParty(event: Event, Party: string): Promise<void> {
            const box = (event.currentTarget as HTMLElement).closest('section')?.querySelector('textarea') ?? undefined;
            must<HTMLElement>(content, '#party-note').textContent = await copyText(Party, box);
        }


        async function save(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            if (current === null)
                return;

            const form     = event.currentTarget as HTMLFormElement;
            const note     = must<HTMLElement>(content, '#grid-operator-note');
            const entity   = current.gridOperator;

            // Read before whileSaving() switches the form off: a field that
            // is off is not in its FormData.
            const name     = field(form, 'name');
            const street   = field(form, 'street');
            const number   = field(form, 'houseNumber');
            const postal   = field(form, 'postalCode');
            const city     = field(form, 'city');
            const country  = field(form, 'country').toUpperCase();

            const anyOfTheAddress = [ street, number, postal, city, country ].some(part => part.length > 0);

            const change: GridOperatorChange = {
                name:      name.length > 0 ? { [languageOf(entity.name)]: name } : null,
                address:   anyOfTheAddress
                               ? { ...(entity.address ?? {}),
                                   street:       street  || undefined,
                                   houseNumber:  number  || undefined,
                                   postalCode:   postal  || undefined,
                                   city:         city.length > 0 ? { [languageOf(entity.address?.city)]: city } : undefined,
                                   country:      country || undefined }
                               : null,
                homepage:  field(form, 'homepage') || null,
                hotline:   field(form, 'hotline')  || null
            };

            note.textContent = '';
            must<HTMLElement>(content, '#grid-operator-error').textContent = '';

            try
            {

                current = await whileSaving(content, note, () => api.gridOperator.save(change));

                if (cancelled)
                    return;

                draw();
                form.reset();

                note.textContent = 'Saved.';

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#grid-operator-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {
                const loaded = await api.gridOperator.get();

                if (!cancelled) {
                    current = loaded;
                    draw();
                }
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The grid operator could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        /**
         * Loaded anew - Reload - is what the grid operator has, the form too,
         * which a draw on its own would leave as typed.
         */
        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#grid-operator-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
