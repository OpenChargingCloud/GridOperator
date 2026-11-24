import { nodeAPI,
         request,
         type Certificate        as NodeCertificate,
         type CertificateImport  as NodeCertificateImport,
         type CertificateStore   as NodeCertificateStore,
         type NodeConfiguration,
         type NodeMe,
         type NodeResource,
         type NodeStatus,
         type Operation }  from '@node/api/client';


// What every node answers - the log, name resolution, the time, the store,
// who is signed in - and how it is asked are WWCP_Node's, and every page here
// reads them from this module. What follows is what a grid operator says of
// its own: the grid operator it is in POI 3, its grid connection points, the
// roaming network it publishes to, and its OpenADR 3 VTN.
export * from '@node/api/client';


/** What a role may be allowed to touch on this grid operator: what every node has, its points of interest and OpenADR. */
export type Resource = NodeResource | 'poi' | 'openadr';

/** What somebody signed in to this grid operator may do: an operation on a resource, written "poi:edit". */
export type Permission = `${Resource}:${Operation}`;

/** Who is signed in to the web interface. */
export type Me = NodeMe<Resource>;

/** How the grid operator is doing right now: what every node says, and nothing more yet. */
export type Status = NodeStatus;

/**
 * What the grid operator is made of: every node's sections, and its own. Only
 * the shape the Configuration page relies on is named.
 */
export interface Configuration extends NodeConfiguration {
    gridOperator:  Record<string, unknown>;
}

/** What a certificate in the store is for: the roots it believes, the server certificates it holds a server to, who it is as a client, and what its web interface shows over HTTPS. */
export type CertificateKind = 'tlsRoot' | 'tlsServer' | 'tlsIdentity' | 'tlsServerIdentity';

/** One certificate in the store. */
export type Certificate = NodeCertificate<CertificateKind>;

/** What an import into the store sends. */
export type CertificateImport = NodeCertificateImport<CertificateKind>;

/** The store, grouped the way it is shown. */
export type CertificateStore = NodeCertificateStore<CertificateKind>;


/** A text in several languages, as POI writes one: { "de": "…", "en": "…" }. */
export type I18N = Record<string, string>;

/** An address, as Styx writes one embedded. */
export interface Address {
    street?:       string;
    houseNumber?:  string;
    postalCode?:   string;
    city?:         I18N;
    country?:      string;
    [key: string]: unknown;
}

/** One public key of the grid operator, as POI 3 writes one. */
export interface PublicKey {
    '@id':  string;
    key:    string;
}

/** The grid operator, as a roaming network embeds it - without its grid connection points. */
export interface GridOperatorEntity {
    '@id':          string;
    name?:          I18N;
    description?:   I18N;
    address?:       Address;
    homepage?:      string;
    hotline?:       string;
    signingKeys:    PublicKey[];
    identityKeys:   PublicKey[];
    created:        string;
    lastChange:     string;
}

/** The grid operator, as the page "Grid operator" shows it. */
export interface GridOperatorInfo {
    gridOperator:          GridOperatorEntity;
    gridConnectionPoints:  number;
    signingKey:            { id: string; curve: string };
    identityKey:           { id: string; curve: string };
    /** What the operator of a roaming network adds it with. */
    party:                 Record<string, unknown>;
    changeable:            string[];
    directory:             string;
}

/** What a page may change of the grid operator: a null or empty one is removed. */
export interface GridOperatorChange {
    name?:      I18N | null;
    address?:   Address | null;
    homepage?:  string | null;
    hotline?:   string | null;
}

/** A limit, as the JSON API shows it. */
export interface Limit {
    id:                     string;
    name:                   string | null;
    kind:                   string;
    limitKW:                number;
    start:                  string;
    end:                    string;
    state:                  'scheduled' | 'active' | 'over';
    priority:               number | null;
    gridConnectionPointId:  string | null;
    localControllers:       string[];
    sentBy:                 string;
    created:                string;
    event:                  Record<string, unknown>;
}

/** One grid connection point, as POI 3 writes it, with the local controllers behind it and its limits not over yet. */
export interface GridConnectionPoint {
    '@id':                           string;
    name?:                           I18N;
    description?:                    I18N;
    address?:                        Address;
    voltageLevel?:                   string;
    connectionType?:                 string;
    nominalVoltage?:                 number;
    nominalFrequency?:               number;
    contractedImportPower?:          number;
    contractedExportPower?:          number;
    contractedImportApparentPower?:  number;
    contractedExportApparentPower?:  number;
    connectionAgreementId?:          string;
    networkLocationId?:              string;
    marketLocationIds?:              string[];
    meteringLocationIds?:            string[];
    localControllers:                string[];
    limits:                          Limit[];
}

/** Every grid connection point, and what a form offers for one. */
export interface GridConnectionPoints {
    gridOperatorId:        string;
    gridConnectionPoints:  GridConnectionPoint[];
    voltageLevels:         string[];
    connectionTypes:       string[];
}

/** Where the grid operator publishes itself to. */
export interface RoamingNetworkSettings {
    url:             string;
    maxURLLength:    number;
    gridOperatorId:  string;
    file:            string;
}

/** One step of a publication. */
export interface PublicationStep {
    kind:        'add' | 'update' | 'remove' | 'untouched';
    entityType:  string;
    entityId:    string;
    property:    string | null;
    text:        string;
}

/** What a publication would change, or changed. */
export interface Publication {
    url:       string;
    success:   boolean;
    message:   string;
    isParty:   boolean | null;
    revision:  number | null;
    changes:   number;
    steps:     PublicationStep[];
    answer:    Record<string, unknown> | null;
}

/** The OpenADR 3 VTN, as its page leads with it. */
export interface OpenADRInfo {
    vtnURL:              string;
    tokenURL:            string;
    program:             Record<string, unknown>;
    kinds:               string[];
    unit:                string;
    maxDurationMinutes:  number;
    localControllers:    number;
    signedIn:            number;
    activeLimits:        number;
    scheduledLimits:     number;
    reports:             number;
    file:                string;
}

/**
 * A connection a local controller connects to the grid operator over: who
 * signs in, over what, in which binding of OpenADR - its REST API in a tunnel,
 * or its JSON messages -, whether it is switched on, where it stands, and how
 * many notifications wait to be told over it.
 */
export interface VENConnection {
    id:                    string;
    login:                 string | null;
    description:           string | null;
    transport:             'ws' | 'wss' | 'ssh';
    binding:               'rest' | 'messages';
    enabled:               boolean;
    /** connected, waiting, refused, ... - or null, where nothing was said of it yet. */
    status:                string | null;
    said:                  string | null;
    since:                 string | null;
    in:                    boolean;
    notificationsWaiting:  number;
}

/**
 * A subscription of a local controller, as OpenADR 3 writes it, without a
 * bearer token: each callback over the connection it was made over, or to a
 * webhook.
 */
export interface VENSubscription {
    id:                string | null;
    clientName:        string;
    created:           string | null;
    objectOperations:  { objects: string[]; operations: string[]; callbackUrl: string; callback: 'connection' | 'webhook' }[];
    /** The connection it is told over, where it was made over one. */
    toldOver:          string | null;
}

/** A local controller that may sign in at the VTN. */
export interface LocalController {
    venID:                  string;
    venName:                string;
    chargingPoolId:         string;
    gridConnectionPointId:  string;
    description?:           string;
    created:                string;
    lastSeen:               string | null;
    limits:                 string[];
    reports:                number;
    /** The connections its VEN connects to the grid operator over. */
    connections:            VENConnection[];
    /** Its subscriptions, oldest first. */
    subscriptions:          VENSubscription[];
    /** The targets it may see. */
    targets:                string[];
    /** Only in the answer that made it, or a new secret for it. */
    credentials?:           { tokenURL: string; vtnURL: string; clientID: string; clientSecret: string };
}

/** Every local controller, and where they sign in. */
export interface LocalControllers {
    tokenURL:          string;
    vtnURL:            string;
    /** Over which ways a local controller may be let connect now: the WebSocket port of OpenADR, SSH. */
    connectable?:      { ws: boolean; ssh: boolean };
    localControllers:  LocalController[];
}

/** A connection to be made for a local controller: over the WebSocket port or SSH, in a binding of OpenADR. */
export interface NewVENConnection {
    transport:     'ws' | 'ssh';
    binding:       'messages' | 'rest';
    /** Over SSH: its public key, as one line of an authorized_keys file - rather than a password made. */
    sshKey?:       string;
    description?:  string;
}

/**
 * A connection made for a local controller, and what it connects with - named
 * as the local controller has them: the URL is its "vtnURL" and the URL of its
 * connection to the grid operator. The password is said this once.
 */
export interface VENConnectionMade extends VENConnection {
    venName:      string;
    credentials:  { vtnURL: string; openADRBinding: 'messages' | 'rest'; login: string; password?: string; sshHostKey?: string };
}

/** A local controller to be added. */
export interface NewLocalController {
    venName:                string;
    chargingPoolId:         string;
    gridConnectionPointId:  string;
    description?:           string;
}

/** A limit to be sent. */
export interface NewLimit {
    kind:                    string;
    limitKW:                 number;
    durationMinutes:         number;
    start?:                  string;
    gridConnectionPointId?:  string;
    name?:                   string;
}

/** Every limit, the newest first. */
export interface Limits {
    timestamp:  string;
    limits:     Limit[];
}

/** Every report kept, the newest first, as OpenADR 3 writes them. */
export interface Reports {
    capacity:  number;
    reports:   Record<string, unknown>[];
}


const point  = (id: string) => `/gridConnectionPoints/${encodeURIComponent(id)}`;
const lc     = (name: string) => `/openADR/localControllers/${encodeURIComponent(name)}`;

/** How the grid operator is asked: the routes every node has, typed with the grid operator's own of them, and its own. */
export const api = {

    ...nodeAPI<{ me: Me; status: Status; configuration: Configuration; kind: CertificateKind; store: CertificateStore }>(),

    gridOperator: {
        get:     ()                                  => request<GridOperatorInfo>     ('GET',    '/gridOperator'),
        save:    (change: GridOperatorChange)        => request<GridOperatorInfo>     ('PUT',    '/gridOperator', change)
    },

    gridConnectionPoints: {
        list:    ()                                  => request<GridConnectionPoints> ('GET',    '/gridConnectionPoints'),
        get:     (id: string)                        => request<GridConnectionPoint>  ('GET',    point(id)),
        add:     (json: Record<string, unknown>)     => request<GridConnectionPoint>  ('POST',   '/gridConnectionPoints', json),
        save:    (id: string, json: Record<string, unknown>) => request<GridConnectionPoint> ('PUT', point(id), json),
        remove:  (id: string)                        => request<GridConnectionPoints> ('DELETE', point(id))
    },

    roamingNetwork: {
        settings:      ()                            => request<RoamingNetworkSettings> ('GET',  '/configuration/roamingNetwork'),
        saveSettings:  (settings: { url: string })   => request<RoamingNetworkSettings> ('PUT',  '/configuration/roamingNetwork', settings),
        preview:       ()                            => request<Publication>            ('GET',  '/roamingNetwork/publication'),
        publish:       ()                            => request<Publication>            ('POST', '/roamingNetwork/publication', {})
    },

    openADR: {
        get:               ()                        => request<OpenADRInfo>       ('GET',    '/openADR'),
        localControllers:  ()                        => request<LocalControllers>  ('GET',    '/openADR/localControllers'),
        addLocalController: (json: NewLocalController) => request<LocalController> ('POST',   '/openADR/localControllers', json),
        newSecret:         (name: string)            => request<LocalController>   ('POST',   `${lc(name)}/secret`, {}),
        removeLocalController: (name: string)        => request<LocalControllers>  ('DELETE', lc(name)),
        connect:           (name: string, json: NewVENConnection) => request<VENConnectionMade> ('POST', `${lc(name)}/connections`, json),
        disconnect:        (name: string, id: string) => request<LocalControllers> ('DELETE', `${lc(name)}/connections/${encodeURIComponent(id)}`),
        limits:            ()                        => request<Limits>            ('GET',    '/openADR/limits'),
        sendLimit:         (json: NewLimit)          => request<Limit>             ('POST',   '/openADR/limits', json),
        cancelLimit:       (id: string)              => request<Limits>            ('DELETE', `/openADR/limits/${encodeURIComponent(id)}`),
        reports:           ()                        => request<Reports>           ('GET',    '/openADR/reports')
    }

};


/**
 * The WebSocket server the local controllers connect to - their VENs,
 * speaking OpenADR over the connection: the REST API in a tunnel, or in
 * messages -, as the grid operator answers at "/configuration/ven-server".
 * Its logins, their groups and the chains a client certificate may lead to
 * are every WebSocket server's, below it: see WWCP_Node's
 * connecting/api/webSocketLogins.
 */
export interface VENServer {
    /** The "webSocketServer" section, every field filled in: "enabled", "port", "clientAuthentication", "ssh", ... */
    configuration:  {
        enabled?:               boolean;
        address?:               string;
        port?:                  number;
        clientAuthentication?:  ('httpAuthWithoutTLS' | 'httpAuthOverTLS' | 'tlsClientCertificate')[];
        checkCertificateRevocation?: boolean;
        ssh?:                   { enabled: boolean };
    };
    state: {
        /** Whether the WebSocket server is listening. */
        running:  boolean;
        /** Whether its port speaks TLS. */
        tls:      boolean;
        /** Whether OpenADR is served over SSH, on the grid operator's SSH server. */
        ssh:      boolean;
        sshPort:  number | null;
    };
    /** The logins connected now, over a WebSocket or SSH. */
    connected:  string[];
}

/** How the server of the local controllers is asked. Changed in the file, and nowhere else. */
export const venServerAPI = {
    get: () => request<VENServer>('GET', '/configuration/ven-server')
};
