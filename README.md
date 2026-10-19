# GridOperator

A grid operator - a distribution system operator (DSO) - in the OpenChargingCloud: a
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node) with a JSON API below `/api`, a web interface at `/`
and an OpenADR 3 VTN at `/openadr3`.

On top of what every node of the family is - the sign-in, DNS, NTS, the SSH server, the certificate store and the
log - it is three things:

- **The grid operator it is in [WWCP POI 3](https://github.com/OpenChargingCloud/WWCP_POI)**, kept for itself:
  made at the first start with a signing key (P-256) and an identity key (P-521) of its own, in `gridOperator/`
  beside the configuration file. Its name, address, homepage and hotline change in the web interface; its
  identification and its keys do not.
- **Its grid connection points**, embedded in it as a roaming network embeds them: where charging pools are
  connected to its grid, at which voltage level, with which contracted power, market and metering locations.
  Both are published to a roaming network - e.g. `http://127.0.0.1:2356/poi/RNs/QA` of a RoamingHub - with one
  change set of its head, signed by the grid operator, once the operator of that network has added it as a party
  with the public halves of its keys.
- **An OpenADR 3 VTN** for the local controllers of the charging pools behind its grid connection points - its
  VENs. Each is added with the pool it controls and the point that pool is connected to, and gets a client secret,
  said once. It signs in with the client credentials of OAuth 2.0, reads the limits meant for it - events of the
  program `grid-capacity`, `IMPORT_CAPACITY_LIMIT` or `EXPORT_CAPACITY_LIMIT` in kW, targeted by `VEN_NAME` - and
  reports what it did.

It is not built alone: [GridOperatorCLI](https://github.com/OpenChargingCloud/GridOperatorCLI) pins it beside
the libraries it needs, builds it, and runs its tests.

| | |
|---|---|
| `GridOperator/GridOperator.cs` | the node: its names, its port (2358), the certificates it keeps |
| `GridOperator/GridOperatorAccess.cs` | the resources `poi` and `openadr`, and the roles `planner` and `dispatcher` |
| `GridOperator/POI/` | the grid operator it is with its keys (`OwnGridOperator`), and what publishing changes (`PublicationPlan`) |
| `GridOperator/GridOperator.POI.cs` | the grid operator and its grid connection points, changed |
| `GridOperator/GridOperator.RoamingNetwork.cs` | the roaming network it publishes to, and the publication: GET of its head, COMMIT of a signed change set |
| `GridOperator/OpenADR/` | the local controllers that may sign in, the limits, the store they are kept in, and the VTN at `/openadr3` |
| `GridOperator/HTTPAPI/` | the JSON API: every node's, and `v1/gridOperator`, `v1/gridConnectionPoints`, `v1/configuration/roamingNetwork`, `v1/roamingNetwork/publication` and `v1/openADR/…` |
| `GridOperator/Frontend/` | the web interface, bundled by webpack with what every node shares |
| `GridOperatorTests/` | what kind of node it is, its keys across a restart, the points, the publication - in memory and over HTTP to the POI API a hub serves -, the VTN as a local controller sees it, the API with its roles, and WWCP_Node's conformance suite asked of a grid operator |

### The OpenADR 3 VTN, as a local controller uses it

```
POST /openadr3/auth/token        grant_type=client_credentials&client_id=<VEN name>&client_secret=<secret>
GET  /openadr3/programs          Authorization: Bearer <token>
GET  /openadr3/events            the limits meant for it, still to come or going on
GET  /openadr3/vens              itself
POST /openadr3/reports           what it did on one of its events
```

A VEN changes no program, event or VEN: that is a 403. Every refusal is a problem of RFC 9457.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
