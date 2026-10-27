import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { html } from '@node/view';
import { nodeMenu, startNode } from '@node/start';

import { configurationPage } from './pages/configuration';
import { gridConnectionPointPage } from './pages/gridConnectionPoint';
import { gridConnectionPointsPage } from './pages/gridConnectionPoints';
import { gridOperatorPage } from './pages/gridOperator';
import { limitsPage } from './pages/limits';
import { localControllersPage } from './pages/localControllers';
import { roamingNetworkPage } from './pages/roamingNetwork';

// What a grid operator has pages for of its own: the grid operator it is in
// POI 3, its grid connection points, and OpenADR - the local controllers of
// the charging pools behind them, and the limits sent to them - with the
// roaming network it publishes itself to below the configuration. Everything
// else is what every node has: name resolution, the time, the SSH server, the
// certificates it believes, who it is as a client and the log - see
// WWCP_Node's start.ts.
startNode({

    name:  'Grid operator',
    icon:  'fa-tower-broadcast',

    menu: [
        { path: '/gridOperator',          label: 'Grid operator',          icon: 'fa-building',         permission: [ 'poi:read' ] },
        { path: '/gridConnectionPoints',  label: 'Grid connection points', icon: 'fa-plug',             permission: [ 'poi:read' ] },
        { path: '/openADR/limits',        label: 'Limits',                 icon: 'fa-gauge-high',       permission: [ 'openadr:read' ] },
        { path: '/openADR/localControllers', label: 'Local controllers',   icon: 'fa-charging-station', permission: [ 'openadr:read' ] },
        nodeMenu.configuration([
            { path: '/configuration/roamingNetwork', label: 'Roaming network', icon: 'fa-share-nodes', permission: [ 'poi:read' ] },
            nodeMenu.dns,
            nodeMenu.nts,
            nodeMenu.ssh,
            nodeMenu.certificates,
            nodeMenu.identities,
            // What the web interface shows over HTTPS, where the "web"
            // section says "tls": { "certificate": "store" }.
            nodeMenu.serverIdentities
        ]),
        nodeMenu.logs
    ],

    // What the roots it believes are for, in a grid operator's words: the
    // servers it talks to - the time and name servers, and the roaming hub it
    // publishes itself to.
    certificates: {
        hints: {
            believes:  html`
                What this grid operator believes, beside the roots of this machine: a time server, a name server
                and the roaming hub it publishes itself to are believed when they chain to one of these - or, held
                to a fingerprint, when they show exactly that certificate.
            `,
            presents:  html`
                Who this grid operator is to a server that asks, with its private key. Kept for a roaming hub that
                asks for a client certificate; nothing here presents it yet. Its local controllers sign in with the
                client credentials of OpenADR 3, not with a certificate.
            `
        }
    },

    // "/" is every node's: the first page of the menu the person signed in may
    // open - the grid operator, for whoever may look at it.
    pages: {

        '/gridOperator':                  gridOperatorPage,
        '/gridConnectionPoints':          gridConnectionPointsPage,
        '/gridConnectionPoints/:id':      gridConnectionPointPage,

        '/openADR/limits':                limitsPage,
        '/openADR/localControllers':      localControllersPage,

        '/configuration':                 configurationPage,
        '/configuration/roamingNetwork':  roamingNetworkPage

    }

});
