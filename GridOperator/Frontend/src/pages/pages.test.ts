/**
 * What the grid operator's own pages are held to: what every page of every
 * kind of node is, by the rules of WWCP_Node's test/pages.ts. Every page of it
 * has a form but the configuration.
 *
 * Run with `npm test`. "@node/.." is WWCP_Node/Frontend, where the rules are.
 */

import { everyPageIn } from '@node/../test/pages.ts';


everyPageIn(new URL('./', import.meta.url), {
    withForms: [ 'gridConnectionPoint.ts', 'gridConnectionPoints.ts', 'gridOperator.ts', 'limits.ts', 'localControllers.ts', 'roamingNetwork.ts' ]
});
