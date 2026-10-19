/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of GridOperator <https://github.com/OpenChargingCloud/GridOperator>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.SecureShell;

using cloud.charging.open.GridOperator.OpenADR;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// A grid operator - a distribution system operator (DSO) - as a WWCP
    /// node: with its JSON API at "/api", its web interface at "/", and the
    /// OpenADR 3 VTN the local controllers of the charging pools on its grid
    /// talk to at "/openadr3".
    /// </summary>
    /// <remarks>
    /// <para>
    /// What every program of this family has - the sign-in, the name servers,
    /// the time servers, the certificates it holds them to, the SSH server and
    /// the log - the node below brings. What makes it a grid operator is on
    /// top, in three parts.
    /// </para>
    /// <para>
    /// First, the grid operator it is in WWCP POI 3, made at the first start
    /// with keys of its own and kept for itself: its name, its address, its
    /// homepage and its hotline - see <see cref="POI.OwnGridOperator"/>.
    /// Second, its grid connection points, embedded in it as a roaming network
    /// embeds them: where charging pools are connected to its grid, at which
    /// voltage level and with which contracted power. Both are published to a
    /// roaming network with change sets it signs itself, once its operator has
    /// added it as a party with the public halves of its keys.
    /// </para>
    /// <para>
    /// Third, OpenADR 3: the local controllers of the charging pools behind
    /// its grid connection points are its VENs. Each is added here with the
    /// pool it controls and the point that pool is connected to, and signs in
    /// with the secret it was given then; it reads the limits sent to the
    /// local controllers behind its point - the capacity it may take from the
    /// grid, or feed into it - and reports what it did.
    /// </para>
    /// </remarks>
    public partial class GridOperator : WWCPNode
    {

        #region Data

        /// <summary>
        /// The manifest resource prefix of the embedded frontend bundle
        /// (see the EmbedFrontend target of GridOperator.csproj).
        /// </summary>
        public const            String    HTTPRoot         = "cloud.charging.open.GridOperator.HTTPRoot.";

        /// <summary>
        /// The TCP port the web interface listens on, unless another is given.
        /// </summary>
        /// <remarks>
        /// Clear of the smartphone's 2346, the vehicle's 2347, the station's
        /// 2348, the local controller's 2350, the CSMS's 2351, the gateway's
        /// 2353, the e-mobility provider's 2355, the hub's 2356 and the PKI's
        /// 2360, so that a grid operator started on the same bench as all of
        /// them - the local controllers it talks to among them - does not fight
        /// any of them over a port.
        /// </remarks>
        public static new readonly  IPPort    DefaultHTTPPort  = IPPort.Parse(2358);

        /// <summary>
        /// What kind of node a grid operator is.
        /// </summary>
        /// <remarks>
        /// "GridOperator" after "OpenChargingCloud" in the Server header,
        /// "gridoperator-2026-10-19.log" for a day's log file, and
        /// "GridOperator" as the organization its accounts are in - which a
        /// first start writes into the accounts file and every start after it
        /// reads back, so it is the one that must never change.
        /// </remarks>
        public static readonly      NodeKind  GridOperatorKind = new (
                                                                     Name:           "grid operator",
                                                                     Tag:            "gridOperator",
                                                                     Product:        "GridOperator",
                                                                     Organization:   "GridOperator",
                                                                     LogFilePrefix:  "gridoperator"
                                                                 );

        /// <summary>
        /// The kinds of certificate the grid operator's store keeps: the roots
        /// it believes, the server certificates it holds a server to, and who
        /// it is as a client - of a roaming hub, say.
        /// </summary>
        /// <remarks>
        /// The local controllers sign in with the client credentials of
        /// OpenADR 3, not with a certificate, so there is no root of clients to
        /// keep; and nothing of ISO 15118, which is a vehicle's.
        /// </remarks>
        public static readonly      IReadOnlyList<CertificateKind>  CertificateKinds  = [
                                                                                          CertificateKind.TLSRoot,
                                                                                          CertificateKind.TLSServer,
                                                                                          CertificateKind.TLSIdentity
                                                                                      ];

        #endregion

        #region Properties

        /// <summary>
        /// The JSON API the browser talks to.
        /// </summary>
        public GridOperatorHTTPAPI  API      { get; }

        /// <summary>
        /// The OpenADR 3 VTN the local controllers talk to.
        /// </summary>
        public OpenADRVTNAPI        VTN      { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// One grid operator, with its web interface and its OpenADR 3 VTN.
        /// </summary>
        /// <param name="HTTPHostname">The address the web interface listens on; 127.0.0.1 by default.</param>
        /// <param name="HTTPPort">The TCP port it listens on; <see cref="DefaultHTTPPort"/> by default.</param>
        /// <param name="HTTPServer">An HTTP server to register within, or null to make one.</param>
        /// <param name="BasePath">What everything of this grid operator sits below; the root by default.</param>
        /// <param name="HTTPRootPath">Where the JSON API sits; "/api" below <paramref name="BasePath"/> by default.</param>
        /// <param name="ExtAPI">An HTTPExt API to sign in against, or null for one of this grid operator's own.</param>
        /// <param name="AccountsPath">The directory the accounts live in between starts.</param>
        /// <param name="ConfigFile">Where the configuration lives between starts: one file, whose sections the node below reads.</param>
        /// <param name="DNSClient">How to resolve names, or null to make a client.</param>
        /// <param name="NTSClient">Where to read the time, or null to make a client.</param>
        /// <param name="Frontend">Where the web interface comes from, or null for the embedded bundle.</param>
        /// <param name="CertificatesPath">The directory the certificate store lives in between starts.</param>
        /// <param name="Log">Where everything that happens is written, or null to make a log.</param>
        /// <param name="LogToConsole">Whether the log is also written to the console.</param>
        /// <param name="ConsoleLogLevel">How much of it reaches the console.</param>
        /// <param name="LogPath">The directory the log files are written to, or null to write none.</param>
        /// <param name="BridgeDebugLog">Whether what the libraries below write with DebugX is picked up.</param>
        /// <param name="TimeProvider">The clock, or null for the system one.</param>
        /// <param name="SSH">What the program says about serving the command line over SSH.</param>
        public GridOperator(IIPAddress?            HTTPHostname       = null,
                            IPPort?                HTTPPort           = null,
                            HTTPServer?            HTTPServer         = null,
                            HTTPPath?              BasePath           = null,
                            HTTPPath?              HTTPRootPath       = null,
                            HTTPExtAPI?            ExtAPI             = null,
                            String?                AccountsPath       = null,
                            WWCPConfigFile?        ConfigFile         = null,
                            DNSClient?             DNSClient          = null,
                            NTSClient?             NTSClient          = null,
                            IStaticContentSource?  Frontend           = null,
                            String?                CertificatesPath   = null,
                            EventLog?              Log                = null,
                            Boolean                LogToConsole       = true,
                            LogLevel               ConsoleLogLevel    = LogLevel.Info,
                            String?                LogPath            = null,
                            Boolean                BridgeDebugLog     = true,
                            TimeProvider?          TimeProvider       = null,
                            SSHSettings?           SSH                = null)

            : base(Kind:               GridOperatorKind,
                   Version:            typeof(GridOperator).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                   HTTPPort:           HTTPPort ?? DefaultHTTPPort,
                   HTTPHostname:       HTTPHostname,
                   HTTPServer:         HTTPServer,
                   BasePath:           BasePath,
                   HTTPRootPath:       HTTPRootPath,
                   ExtAPI:             ExtAPI,
                   AccountsPath:       AccountsPath,
                   ConfigFile:         ConfigFile,
                   DNSClient:          DNSClient,
                   NTSClient:          NTSClient,
                   Frontend:           Frontend ?? new EmbeddedContentSource(HTTPRoot, typeof(GridOperator).Assembly),
                   Resources:          GridOperatorAccess.Resources,
                   RoleDefinitions:    GridOperatorAccess.Roles,
                   CertificatesPath:   CertificatesPath,
                   CertificateKinds:   CertificateKinds,
                   Log:                Log,
                   LogToConsole:       LogToConsole,
                   ConsoleLogLevel:    ConsoleLogLevel,
                   LogPath:            LogPath,
                   BridgeDebugLog:     BridgeDebugLog,
                   TimeProvider:       TimeProvider,
                   SSH:                SSH)

        {

            // Beside the configuration file rather than below the working
            // directory, for the reason the certificate store is: a grid
            // operator that made new keys because "dotnet run" was typed
            // somewhere else would be a different grid operator.
            var beside = Path.GetDirectoryName(Path.GetFullPath(this.ConfigFile.Path)) ?? ".";

            #region The grid operator it is, and the roaming network it publishes to

            BuildOwnGridOperator(Path.Combine(beside, POI.OwnGridOperator.DirectoryName));

            this.roamingNetworkSettings = LoadRoamingNetworkSettings();

            #endregion

            #region OpenADR

            this.OpenADR = new OpenADRStore(Path.Combine(beside, OpenADRStore.DirectoryName, OpenADRStore.FileName), this.TimeProvider);

            foreach (var problem in OpenADR.ReadProblems)
                this.Log.Warning(problem, "openadr");

            this.Program = NewProgram();

            this.VTN = new OpenADRVTNAPI(this.HTTPServer,
                                         this,
                                         this.BasePath + OpenADRVTNAPI.PathSegment);

            this.Log.Info($"{OpenADR.Logins().Count} local controller(s) may sign in at the OpenADR VTN, {OpenADR.Events().Count} limit(s) are kept in '{OpenADR.FilePath}'.",
                          "openadr");

            #endregion

            #region The JSON API

            this.Log.Info(
                OwnsExtAPI
                    ? $"The accounts of this grid operator are in '{this.ExtAPI.DatabaseFileName}', its HTTPExt API at '{this.ExtAPI.RootPath}'."
                    : $"This grid operator signs in against accounts it shares, at '{this.ExtAPI.RootPath}'.",
                "web", "http"
            );

            // The JSON API at "/api", beside the web interface the node below
            // has already put at "/" and the VTN at "/openadr3".
            this.API = new GridOperatorHTTPAPI(
                           HTTPServer:    this.HTTPServer,
                           GridOperator:  this,
                           ExtAPI:        this.ExtAPI,
                           Log:           this.Log,
                           APIPath:       this.HTTPRootPath,
                           Version:       Version
                       );

            #endregion

        }

        #endregion


        #region (protected override) OnStarted()

        /// <summary>
        /// What a grid operator says once it is up: where its API and its VTN are.
        /// </summary>
        protected override Task OnStarted()
        {

            Log.Info($"The JSON API is at {APIURL}v1/status, the OpenADR 3 VTN at {OpenADRVTNURL}.", "web", "http", "openadr");

            return Task.CompletedTask;

        }

        #endregion


        #region ConfigurationJSON()

        /// <summary>
        /// What this grid operator is, as the Configuration page of the web
        /// interface reads it: what the node below says of itself, and on top
        /// the grid operator.
        /// </summary>
        public override JObject ConfigurationJSON()
        {

            var json = base.ConfigurationJSON();

            // First, because it is the card the page leads with.
            json.AddFirst(new JProperty("gridOperator", new JObject(
                              new JProperty("version",               Version),
                              new JProperty("createdAt",             CreatedAt.ToString("o")),
                              new JProperty("machine",               Environment.MachineName),
                              new JProperty("runtime",               Environment.Version.ToString()),
                              new JProperty("os",                    Environment.OSVersion.ToString()),
                              new JProperty("gridOperatorId",        Own.Id.ToString()),
                              new JProperty("gridConnectionPoints",  Own.Entity.GridConnectionPoints.Count),
                              new JProperty("roamingNetwork",        RoamingNetworkSettings.IsSet ? RoamingNetworkSettings.URL : "none"),
                              new JProperty("openADRVTN",            OpenADRVTNURL),
                              new JProperty("localControllers",      OpenADR.Logins().Count)
                          )));

            return json;

        }

        #endregion

    }

}
