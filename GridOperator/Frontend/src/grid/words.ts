import type { I18N, Limit } from '../api/client';

/**
 * How a grid operator's pages say what POI 3 and OpenADR 3 write: a text of
 * several languages, a power in W as kW, a moment, and what a limit is.
 */

/** The first text of a text in several languages, or what is given instead. */
export function firstText(Text: I18N | undefined | null, Otherwise = ''): string {
    const texts = Object.values(Text ?? {}).filter(text => typeof text === 'string' && text.length > 0);
    return texts[0] ?? Otherwise;
}

/** The language a text in several languages is in: its first one, or 'en'. */
export function languageOf(Text: I18N | undefined | null): string {
    return Object.keys(Text ?? {})[0] ?? 'en';
}

/** A power in W, said in kW: "110 kW", or "-" where it is unknown. */
export function kW(Watts: number | null | undefined): string {
    return typeof Watts === 'number' && Number.isFinite(Watts)
               ? `${(Watts / 1000).toLocaleString(undefined, { maximumFractionDigits: 3 })} kW`
               : '-';
}

/** A power in kW, as it is. */
export function inKW(KW: number): string {
    return `${KW.toLocaleString(undefined, { maximumFractionDigits: 3 })} kW`;
}

/** A moment, as day and time in the browser's words. */
export function dayAndTime(ISO: string | null | undefined): string {
    if (!ISO)
        return '-';
    const date = new Date(ISO);
    return Number.isNaN(date.getTime())
               ? ISO
               : date.toLocaleString(undefined, { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
}

/** What a kind of limit is, in words. */
export function kindWord(Kind: string): string {
    switch (Kind) {
        case 'IMPORT_CAPACITY_LIMIT':  return 'taken from the grid';
        case 'EXPORT_CAPACITY_LIMIT':  return 'fed into the grid';
        default:                       return Kind;
    }
}

/** The word of the state of a limit. */
export function stateWord(State: Limit['state']): string {
    switch (State) {
        case 'scheduled':  return 'scheduled';
        case 'active':     return 'active';
        default:           return 'over';
    }
}

/** A list typed as text, comma or line separated, as an array without empty entries. */
export function listOf(Text: string): string[] {
    return Text.split(/[,\n]/).map(entry => entry.trim()).filter(entry => entry.length > 0);
}

/** More than one of what the given noun names: "local controllers", "authorities" - as WWCP_Node's NodeWording.Plural says it. */
export function plural(Noun: string): string {
    if (Noun.length > 1 && Noun.endsWith('y') && !'aeiou'.includes(Noun[Noun.length - 2]!))
        return `${Noun.slice(0, -1)}ies`;
    if (Noun.endsWith('s') || Noun.endsWith('x') || Noun.endsWith('ch') || Noun.endsWith('sh'))
        return `${Noun}es`;
    return `${Noun}s`;
}

/** How many of something there are: "1 limit", "0 limits", "3 local controllers" - not "(s)". */
export function counted(Count: number, Noun: string): string {
    return `${Count} ${Count === 1 ? Noun : plural(Noun)}`;
}
