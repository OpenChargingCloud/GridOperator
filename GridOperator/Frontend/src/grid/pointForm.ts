import type { GridConnectionPoint } from '../api/client';
import { firstText, languageOf, listOf } from './words';
import { field, numberField } from '@node/ui';
import { html, nothing, type TemplateResult } from '@node/view';

/**
 * The fields of a grid connection point, as the page that adds one and the
 * page that changes one both draw them - and what they say, read back as
 * POI 3 writes a point: in W, VA, V and Hz, a field left empty not given.
 */

/** What the fields are drawn with. */
export interface PointFields {
    /** The point as it is, or null for a new one. */
    point:            GridConnectionPoint | null;
    voltageLevels:    readonly string[];
    connectionTypes:  readonly string[];
    disabled:         boolean;
}

/** In kW, as the fields are typed - or empty. */
function kWOf(Watts: number | undefined): string {
    return typeof Watts === 'number' ? String(Watts / 1000) : '';
}

/** The fields, drawn. */
export function pointFields({ point, voltageLevels, connectionTypes, disabled }: PointFields): TemplateResult {

    const voltage     = point?.voltageLevel   ?? '';
    const connection  = point?.connectionType ?? '';

    return html`

        <div class="form-grid">

            ${point === null ? html`
                <label>Identification
                    <input type="text" name="id" required maxlength="100" placeholder="GCP-0815" ?disabled=${disabled} />
                </label>
            ` : nothing}

            <label>Name
                <input type="text" name="name" value="${firstText(point?.name)}" maxlength="200" ?disabled=${disabled} />
            </label>

            <label>Voltage level
                <select name="voltageLevel" ?disabled=${disabled}>
                    <option value="" ?selected=${voltage === ''}>unknown</option>
                    ${voltageLevels.map(level => html`<option value="${level}" ?selected=${level === voltage}>${level}</option>`)}
                </select>
            </label>

            <label>Connection
                <select name="connectionType" ?disabled=${disabled}>
                    <option value="" ?selected=${connection === ''}>unknown</option>
                    ${connectionTypes.map(type => html`<option value="${type}" ?selected=${type === connection}>${type}</option>`)}
                </select>
            </label>

            <label>Nominal voltage (V)
                <input type="number" name="nominalVoltage" min="1" step="any" value="${point?.nominalVoltage ?? ''}" ?disabled=${disabled} />
            </label>

            <label>Nominal frequency (Hz)
                <input type="number" name="nominalFrequency" min="1" step="any" value="${point?.nominalFrequency ?? ''}" ?disabled=${disabled} />
            </label>

            <label>Contracted import (kW)
                <input type="number" name="importKW" min="0" step="any" value="${kWOf(point?.contractedImportPower)}" ?disabled=${disabled} />
            </label>

            <label>Contracted export (kW)
                <input type="number" name="exportKW" min="0" step="any" value="${kWOf(point?.contractedExportPower)}" ?disabled=${disabled} />
            </label>

            <label>Contracted import apparent (kVA)
                <input type="number" name="importKVA" min="0" step="any" value="${kWOf(point?.contractedImportApparentPower)}" ?disabled=${disabled} />
            </label>

            <label>Contracted export apparent (kVA)
                <input type="number" name="exportKVA" min="0" step="any" value="${kWOf(point?.contractedExportApparentPower)}" ?disabled=${disabled} />
            </label>

            <label>Connection agreement
                <input type="text" name="connectionAgreementId" value="${point?.connectionAgreementId ?? ''}" ?disabled=${disabled} />
            </label>

            <label>Network location
                <input type="text" name="networkLocationId" value="${point?.networkLocationId ?? ''}" placeholder="E1234567890" ?disabled=${disabled} />
            </label>

            <label class="wide">Market locations
                <input type="text" name="marketLocationIds" value="${(point?.marketLocationIds ?? []).join(', ')}" placeholder="50123456789, ..." ?disabled=${disabled} />
            </label>

            <label class="wide">Metering locations
                <input type="text" name="meteringLocationIds" value="${(point?.meteringLocationIds ?? []).join(', ')}" placeholder="DE000..., ..." ?disabled=${disabled} />
            </label>

        </div>

    `;

}

/**
 * What the fields say, as POI 3 writes a point - with what the point had of
 * its own that the fields do not show, its address and its energy meter say,
 * kept as they were.
 */
export function pointFrom(Form: HTMLFormElement, Before: GridConnectionPoint | null): Record<string, unknown> {

    const json: Record<string, unknown> = {};

    if (Before !== null)
        for (const [ key, value ] of Object.entries(Before))
            if (key !== 'localControllers' && key !== 'limits')
                json[key] = value;

    json['@id'] = Before?.['@id'] ?? field(Form, 'id');

    const text  = (key: string, value: string) => { if (value.length > 0) json[key] = value; else delete json[key]; };
    const num   = (key: string, value: number, factor = 1) => { if (Number.isFinite(value)) json[key] = value * factor; else delete json[key]; };
    const list  = (key: string, value: string[]) => { if (value.length > 0) json[key] = value; else delete json[key]; };

    const name = field(Form, 'name');

    if (name.length > 0)
        json['name'] = { [languageOf(Before?.name)]: name };
    else
        delete json['name'];

    text('voltageLevel',                   field(Form, 'voltageLevel'));
    text('connectionType',                 field(Form, 'connectionType'));
    num ('nominalVoltage',                 numberField(Form, 'nominalVoltage'));
    num ('nominalFrequency',               numberField(Form, 'nominalFrequency'));
    num ('contractedImportPower',          numberField(Form, 'importKW'),  1000);
    num ('contractedExportPower',          numberField(Form, 'exportKW'),  1000);
    num ('contractedImportApparentPower',  numberField(Form, 'importKVA'), 1000);
    num ('contractedExportApparentPower',  numberField(Form, 'exportKVA'), 1000);
    text('connectionAgreementId',          field(Form, 'connectionAgreementId'));
    text('networkLocationId',              field(Form, 'networkLocationId'));
    list('marketLocationIds',              listOf(field(Form, 'marketLocationIds')));
    list('meteringLocationIds',            listOf(field(Form, 'meteringLocationIds')));

    return json;

}
