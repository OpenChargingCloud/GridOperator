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

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.OpenADRv3;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// What the grid operator's OpenADR 3 VTN keeps: the local controllers
    /// that may sign in, the limits sent to them, the reports they sent back,
    /// and the access tokens handed out to them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The logins and the limits are kept in one file, "openADR/openadr.json"
    /// beside the configuration file, written whole after every change - first
    /// beside it, then in its place. The reports and the tokens are not: a
    /// report is what a local controller says it did, worth looking at while it
    /// is fresh, and a token is an hour of signing in that a restart asks for
    /// again.
    /// </para>
    /// <para>
    /// Every change is made to a copy and kept only once the file is written:
    /// a disk that refused it leaves the store as it was, and says so.
    /// </para>
    /// </remarks>
    public sealed class OpenADRStore
    {

        #region Data

        /// <summary>
        /// The directory, below the directory of the configuration file.
        /// </summary>
        public const String   DirectoryName   = "openADR";

        /// <summary>
        /// The file, in that directory.
        /// </summary>
        public const String   FileName        = "openadr.json";

        /// <summary>
        /// How many reports are kept, the newest.
        /// </summary>
        public const Int32    ReportCapacity  = 500;

        /// <summary>
        /// How long an access token signs in.
        /// </summary>
        public static readonly TimeSpan  TokenLifetime  = TimeSpan.FromHours(1);

        private readonly Lock                                       storeLock  = new ();
        private readonly TimeProvider                               timeProvider;

        private Dictionary<String, LocalControllerLogin>            logins     = new (StringComparer.OrdinalIgnoreCase);
        private Dictionary<Event_Id, GridEvent>                     events     = [];
        private readonly LinkedList<Report>                         reports    = new ();
        private readonly Dictionary<String, (String VENName, DateTimeOffset Expires)>  tokens  = new (StringComparer.Ordinal);
        private readonly Dictionary<String, DateTimeOffset>         lastSeen   = new (StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Properties

        /// <summary>
        /// Where the logins and the limits are kept.
        /// </summary>
        public String                 FilePath      { get; }

        /// <summary>
        /// What could not be read at the start, one line each.
        /// </summary>
        public IReadOnlyList<String>  ReadProblems  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// The store kept in the given file - empty, where there is none yet.
        /// </summary>
        public OpenADRStore(String         FilePath,
                            TimeProvider?  TimeProvider = null)
        {

            this.FilePath      = FilePath;
            this.timeProvider  = TimeProvider ?? TimeProvider.System;

            var problems       = new List<String>();

            if (File.Exists(FilePath))
            {
                try
                {

                    // Timestamps stay the text they were written as: read as
                    // dates, they came back in the words of this machine.
                    using var reader = new JsonTextReader(new StringReader(File.ReadAllText(FilePath))) { DateParseHandling = DateParseHandling.None };
                    var json = JObject.Load(reader);

                    foreach (var loginJSON in (json["localControllers"] as JArray ?? []).OfType<JObject>())
                    {
                        if (LocalControllerLogin.TryParse(loginJSON, out var login, out var error))
                            logins[login.VENName] = login;
                        else
                            problems.Add($"A local controller in '{FilePath}' was not read: {error}");
                    }

                    foreach (var eventJSON in (json["events"] as JArray ?? []).OfType<JObject>())
                    {
                        if (GridEvent.TryParseStored(eventJSON, out var gridEvent, out var error))
                            events[gridEvent.Id] = gridEvent;
                        else
                            problems.Add($"A limit in '{FilePath}' was not read: {error}");
                    }

                }
                catch (Exception e)
                {
                    problems.Add($"'{FilePath}' could not be read, and this grid operator starts without local controllers and limits: {e.Message}");
                }
            }

            ReadProblems = problems;

        }

        #endregion


        #region Logins

        /// <summary>
        /// Every local controller that may sign in, by name.
        /// </summary>
        public IReadOnlyList<LocalControllerLogin> Logins()
        {
            lock (storeLock)
                return [ .. logins.Values.OrderBy(login => login.VENName, StringComparer.OrdinalIgnoreCase) ];
        }

        /// <summary>
        /// The local controller of the given name, or null.
        /// </summary>
        public LocalControllerLogin? Login(String VENName)
        {
            lock (storeLock)
                return logins.GetValueOrDefault(VENName);
        }

        /// <summary>
        /// The local controller of the given VEN identification, or null.
        /// </summary>
        public LocalControllerLogin? LoginById(String VENId)
        {
            lock (storeLock)
                return logins.Values.FirstOrDefault(login => login.VENId.ToString() == VENId);
        }

        /// <summary>
        /// When the given local controller last asked anything, since this start.
        /// </summary>
        public DateTimeOffset? LastSeen(String VENName)
        {
            lock (storeLock)
                return lastSeen.TryGetValue(VENName, out var seen) ? seen : null;
        }

        /// <summary>
        /// Keep the given login - new, or with a new secret.
        /// </summary>
        public Boolean TrySetLogin(LocalControllerLogin              Login,
                                   [NotNullWhen(false)] out String?  Error)
        {
            lock (storeLock)
            {

                var changed = new Dictionary<String, LocalControllerLogin>(logins, StringComparer.OrdinalIgnoreCase) {
                                  [Login.VENName] = Login
                              };

                if (!TryWrite(changed, events, out Error))
                    return false;

                logins = changed;

                // A new secret: what was signed in with the old one is not any more.
                foreach (var token in tokens.Where(token => token.Value.VENName.Equals(Login.VENName, StringComparison.OrdinalIgnoreCase)).ToArray())
                    tokens.Remove(token.Key);

                return true;

            }
        }

        /// <summary>
        /// Forget the given local controller, and every token it was handed.
        /// </summary>
        public Boolean TryRemoveLogin(String                            VENName,
                                      [NotNullWhen(false)] out String?  Error)
        {
            lock (storeLock)
            {

                var changed = new Dictionary<String, LocalControllerLogin>(logins, StringComparer.OrdinalIgnoreCase);
                changed.Remove(VENName);

                if (!TryWrite(changed, events, out Error))
                    return false;

                logins = changed;

                foreach (var token in tokens.Where(token => token.Value.VENName.Equals(VENName, StringComparison.OrdinalIgnoreCase)).ToArray())
                    tokens.Remove(token.Key);

                lastSeen.Remove(VENName);

                return true;

            }
        }

        #endregion

        #region Tokens

        /// <summary>
        /// A new access token for the local controller with the given client
        /// identification and secret - or null, where they are not one's.
        /// </summary>
        public (String Token, TimeSpan ExpiresIn)? TryIssueToken(String  ClientId,
                                                                 String  ClientSecret)
        {
            lock (storeLock)
            {

                if (!logins.TryGetValue(ClientId, out var login) || !login.IsSecret(ClientSecret))
                    return null;

                var now = timeProvider.GetUtcNow();

                foreach (var expired in tokens.Where(token => token.Value.Expires <= now).ToArray())
                    tokens.Remove(expired.Key);

                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).
                                    Replace('+', '-').
                                    Replace('/', '_').
                                    TrimEnd('=');

                tokens[token]          = (login.VENName, now + TokenLifetime);
                lastSeen[login.VENName] = now;

                return (token, TokenLifetime);

            }
        }

        /// <summary>
        /// The local controller the given access token was handed to, while it signs in.
        /// </summary>
        public LocalControllerLogin? LoginOfToken(String Token)
        {
            lock (storeLock)
            {

                var now = timeProvider.GetUtcNow();

                if (!tokens.TryGetValue(Token, out var issued) || issued.Expires <= now)
                    return null;

                if (!logins.TryGetValue(issued.VENName, out var login))
                    return null;

                lastSeen[login.VENName] = now;

                return login;

            }
        }

        /// <summary>
        /// How many access tokens sign in now.
        /// </summary>
        public Int32 ActiveTokens()
        {
            lock (storeLock)
            {
                var now = timeProvider.GetUtcNow();
                return tokens.Count(token => token.Value.Expires > now);
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Every limit, the newest first.
        /// </summary>
        public IReadOnlyList<GridEvent> Events()
        {
            lock (storeLock)
                return [ .. events.Values.OrderByDescending(gridEvent => gridEvent.Start) ];
        }

        /// <summary>
        /// The limit of the given identification, or null.
        /// </summary>
        public GridEvent? Event(String EventId)
        {
            lock (storeLock)
                return events.Values.FirstOrDefault(gridEvent => gridEvent.Id.ToString() == EventId);
        }

        /// <summary>
        /// Keep a new limit.
        /// </summary>
        public Boolean TryAddEvent(GridEvent                         GridEvent,
                                   [NotNullWhen(false)] out String?  Error)
        {
            lock (storeLock)
            {

                var changed = new Dictionary<Event_Id, GridEvent>(events) {
                                  [GridEvent.Id] = GridEvent
                              };

                if (!TryWrite(logins, changed, out Error))
                    return false;

                events = changed;
                return true;

            }
        }

        /// <summary>
        /// Forget a limit: the local controllers do not read it any more, which
        /// is how OpenADR 3 cancels an event.
        /// </summary>
        public Boolean TryRemoveEvent(Event_Id                          EventId,
                                      [NotNullWhen(false)] out String?  Error)
        {
            lock (storeLock)
            {

                var changed = new Dictionary<Event_Id, GridEvent>(events);
                changed.Remove(EventId);

                if (!TryWrite(logins, changed, out Error))
                    return false;

                events = changed;
                return true;

            }
        }

        #endregion

        #region Reports

        /// <summary>
        /// Every report kept, the newest first.
        /// </summary>
        public IReadOnlyList<Report> Reports()
        {
            lock (storeLock)
                return [ .. reports ];
        }

        /// <summary>
        /// Keep a report, the newest first; the oldest goes beyond the capacity.
        /// </summary>
        public void AddReport(Report Report)
        {
            lock (storeLock)
            {

                reports.AddFirst(Report);

                while (reports.Count > ReportCapacity)
                    reports.RemoveLast();

            }
        }

        #endregion


        #region (private) TryWrite(Logins, Events, out Error)

        private Boolean TryWrite(IReadOnlyDictionary<String,   LocalControllerLogin>  Logins,
                                 IReadOnlyDictionary<Event_Id, GridEvent>             Events,
                                 [NotNullWhen(false)] out String?                     Error)
        {

            var json = new JObject(
                           new JProperty("localControllers",  new JArray(Logins.Values.OrderBy(login => login.VENName, StringComparer.OrdinalIgnoreCase).
                                                                                       Select (login => login.ToJSON(IncludeSecret: true)))),
                           new JProperty("events",            new JArray(Events.Values.OrderBy(gridEvent => gridEvent.Start).
                                                                                       Select (gridEvent => gridEvent.StoredJSON())))
                       );

            try
            {

                if (Path.GetDirectoryName(FilePath) is String directory && directory.Length > 0)
                    Directory.CreateDirectory(directory);

                var written = FilePath + ".tmp";
                File.WriteAllText(written, json.ToString());
                File.Move(written, FilePath, overwrite: true);

                Error = null;
                return true;

            }
            catch (Exception e)
            {
                Error = $"'{FilePath}' could not be written, and nothing was changed: {e.Message}";
                return false;
            }

        }

        #endregion

    }

}
