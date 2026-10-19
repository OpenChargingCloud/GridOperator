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

using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.POI;

using cloud.charging.open.GridOperator.POI;

using POIGridOperator   = cloud.charging.open.protocols.WWCP.POI.GridOperator;
using POIRoamingNetwork = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;

#endregion

namespace cloud.charging.open.GridOperator
{

    public partial class GridOperator
    {

        #region Data

        /// <summary>
        /// The voltage levels a grid connection point may be at, as POI 3 writes them.
        /// </summary>
        public static readonly IReadOnlyList<String>  VoltageLevels    = [ "LowVoltage", "MediumVoltage", "HighVoltage", "ExtraHighVoltage" ];

        /// <summary>
        /// The kinds of connection a grid connection point may be, as POI 3 writes them.
        /// </summary>
        public static readonly IReadOnlyList<String>  ConnectionTypes  = [ "AC_ThreePhases", "AC_SinglePhase", "AC_SinglePhase_L0", "AC_SinglePhase_L1", "AC_SinglePhase_L2", "Unknown" ];

        #endregion

        #region Properties

        /// <summary>
        /// The grid operator this node is, with its keys and its grid connection points.
        /// </summary>
        public OwnGridOperator  Own    { get; private set; } = default!;

        #endregion


        #region (private) BuildOwnGridOperator(Directory)

        /// <summary>
        /// The grid operator kept in the given directory, or a new one made
        /// there - and said in the log, as its keys are worth a copy.
        /// </summary>
        private void BuildOwnGridOperator(String Directory)
        {

            Own = OwnGridOperator.OpenOrCreate(Directory,
                                               ConfigurationDocument[OwnGridOperator.ConfigurationSection] as JObject,
                                               TimeProvider.GetUtcNow());

            if (Own.IsNew)
                Log.Notice($"This node is the grid operator '{Own.Id}' now: its signing key {Own.SigningKey.Id[..16]}… and its identity key {Own.IdentityKey.Id[..16]}… " +
                           $"were made in '{Directory}'. Keep a copy of them where nobody else can read it: without them, it can never again sign as '{Own.Id}' " +
                            "in a roaming network that knows it.",
                           "poi", "security");

            else
                Log.Info($"This node is the grid operator '{Own.Id}', its keys in '{Directory}', with {Own.Entity.GridConnectionPoints.Count} grid connection point(s).",
                         "poi");

        }

        #endregion


        #region GridOperatorJSON()

        /// <summary>
        /// The grid operator, as the page "Grid operator" shows it: as a
        /// roaming network embeds it - without its grid connection points,
        /// which have a page of their own - its keys, and what the operator of
        /// a roaming network needs to add it.
        /// </summary>
        public JObject GridOperatorJSON()
        {

            var entity  = Own.Entity;
            var json    = entity.ToJSON();

            json.Remove("gridConnectionPoints");

            return new JObject(
                       new JProperty("gridOperator",          json),
                       new JProperty("gridConnectionPoints",  entity.GridConnectionPoints.Count),
                       new JProperty("signingKey",            new JObject(new JProperty("id", Own.SigningKey. Id), new JProperty("curve", "P-256"))),
                       new JProperty("identityKey",           new JObject(new JProperty("id", Own.IdentityKey.Id), new JProperty("curve", "P-521"))),
                       new JProperty("party",                 Own.PartyJSON()),
                       new JProperty("changeable",            new JArray(OwnGridOperator.ChangeableProperties)),
                       new JProperty("directory",             Own.Directory)
                   );

        }

        #endregion

        #region TryUpdateGridOperator(JSON, Who, out Error, out NotSaved)

        /// <summary>
        /// Change what the grid operator says of itself: any of
        /// <see cref="OwnGridOperator.ChangeableProperties"/>, as POI 3 writes
        /// it - a null removes it. Neither its identification, nor its keys,
        /// nor its grid connection points change here.
        /// </summary>
        public Boolean TryUpdateGridOperator(JObject                           JSON,
                                             String                            Who,
                                             [NotNullWhen(false)] out String?  Error,
                                             out Boolean                       NotSaved)
        {

            var unknown = JSON.Properties().
                               Select(property => property.Name).
                               Where (name => !OwnGridOperator.ChangeableProperties.Contains(name)).
                               ToArray();

            if (unknown.Length > 0)
            {
                Error     = $"Only {String.Join(", ", OwnGridOperator.ChangeableProperties.Select(name => $"'{name}'"))} of the grid operator change here, not {String.Join(", ", unknown.Select(name => $"'{name}'"))}.";
                NotSaved  = false;
                return false;
            }

            if (!Own.TryChange(json => {

                                   foreach (var property in JSON.Properties())
                                   {
                                       if (property.Value.Type == JTokenType.Null ||
                                          (property.Value.Type == JTokenType.String && String.IsNullOrWhiteSpace(property.Value.Value<String>())))
                                           json.Remove(property.Name);
                                       else
                                           json[property.Name] = property.Value.DeepClone();
                                   }

                                   return null;

                               },
                               TimeProvider.GetUtcNow(),
                               out _,
                               out Error,
                               out NotSaved))
            {
                return false;
            }

            Log.Notice($"'{Who}' changed {String.Join(", ", JSON.Properties().Select(property => $"'{property.Name}'"))} of the grid operator '{Own.Id}'.",
                       "poi", "web");

            return true;

        }

        #endregion


        #region GridConnectionPointsJSON()

        /// <summary>
        /// Every grid connection point, each with the local controllers behind
        /// it, and the values a page offers for them.
        /// </summary>
        public JObject GridConnectionPointsJSON()

            => new (new JProperty("gridOperatorId",        Own.Id.ToString()),
                    new JProperty("gridConnectionPoints",  new JArray(Own.Entity.GridConnectionPoints.Select(GridConnectionPointJSON))),
                    new JProperty("voltageLevels",         new JArray(VoltageLevels)),
                    new JProperty("connectionTypes",       new JArray(ConnectionTypes)));

        #endregion

        #region GridConnectionPointJSON(GridConnectionPoint)

        /// <summary>
        /// One grid connection point, as POI 3 writes it, with the local
        /// controllers behind it and the limits for them that are not over.
        /// </summary>
        public JObject GridConnectionPointJSON(GridConnectionPoint GridConnectionPoint)
        {

            var id    = GridConnectionPoint.Id.ToString();
            var now   = TimeProvider.GetUtcNow();
            var json  = GridConnectionPoint.ToJSON();

            json.Add(new JProperty("localControllers",  new JArray(LocalControllersAt(id).Select(login => login.VENName))));
            json.Add(new JProperty("limits",            new JArray(OpenADR.Events().
                                                                           Where (gridEvent => id.Equals(gridEvent.GridConnectionPointId, StringComparison.OrdinalIgnoreCase) &&
                                                                                               gridEvent.End > now).
                                                                           Select(gridEvent => gridEvent.ToJSON(now)))));

            return json;

        }

        #endregion

        #region TryGetGridConnectionPoint(Id, out GridConnectionPoint)

        /// <summary>
        /// The grid connection point of the given identification, in any case.
        /// </summary>
        public Boolean TryGetGridConnectionPoint(String                                         Id,
                                                 [NotNullWhen(true)] out GridConnectionPoint?  GridConnectionPoint)
        {

            GridConnectionPoint = Own.Entity.GridConnectionPoints.
                                      FirstOrDefault(point => point.Id.ToString().Equals(Id.Trim(), StringComparison.OrdinalIgnoreCase));

            return GridConnectionPoint is not null;

        }

        #endregion

        #region TryAddGridConnectionPoint(JSON, Who, out GridConnectionPoint, out Error, out Refusal)

        /// <summary>
        /// Add a grid connection point, as POI 3 writes one.
        /// </summary>
        public Boolean TryAddGridConnectionPoint(JObject                                         JSON,
                                                 String                                          Who,
                                                 [NotNullWhen(true)]  out GridConnectionPoint?  GridConnectionPoint,
                                                 [NotNullWhen(false)] out String?               Error,
                                                 out ChangeRefusal                               Refusal)
        {

            GridConnectionPoint  = null;
            Refusal              = ChangeRefusal.Invalid;

            if (!TryParsePoint(JSON, out var point, out Error))
                return false;

            var id = point.Id.ToString();

            if (TryGetGridConnectionPoint(id, out _))
            {
                Error    = $"The grid operator '{Own.Id}' has a grid connection point '{id}' already.";
                Refusal  = ChangeRefusal.Conflict;
                return false;
            }

            if (!Own.TryChange(json => {
                                   var points = json["gridConnectionPoints"] as JArray ?? [];
                                   points.Add(point.ToJSON());
                                   json["gridConnectionPoints"] = points;
                                   return null;
                               },
                               TimeProvider.GetUtcNow(),
                               out _,
                               out Error,
                               out var notSaved))
            {
                Refusal = notSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                return false;
            }

            TryGetGridConnectionPoint(id, out GridConnectionPoint);

            Log.Notice($"'{Who}' added the grid connection point '{id}' to the grid operator '{Own.Id}'.", "poi", "web");

            return GridConnectionPoint is not null;

        }

        #endregion

        #region TryReplaceGridConnectionPoint(Id, JSON, Who, out GridConnectionPoint, out Error, out Refusal)

        /// <summary>
        /// Say anew what a grid connection point is - all of it, as POI 3
        /// writes one; its identification stays what it is.
        /// </summary>
        public Boolean TryReplaceGridConnectionPoint(String                                          Id,
                                                     JObject                                         JSON,
                                                     String                                          Who,
                                                     [NotNullWhen(true)]  out GridConnectionPoint?  GridConnectionPoint,
                                                     [NotNullWhen(false)] out String?               Error,
                                                     out ChangeRefusal                               Refusal)
        {

            GridConnectionPoint  = null;
            Refusal              = ChangeRefusal.Invalid;

            if (!TryGetGridConnectionPoint(Id, out var existing))
            {
                Error    = $"The grid operator '{Own.Id}' has no grid connection point '{Id}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            var id      = existing.Id.ToString();
            var given   = (JObject) JSON.DeepClone();

            if (given["@id"] is JValue { Type: JTokenType.String } givenId &&
                !givenId.Value<String>()!.Trim().Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                Error = $"The grid connection point '{id}' keeps its identification; '{givenId.Value<String>()}' would be another one.";
                return false;
            }

            given["@id"] = id;

            if (!TryParsePoint(given, out var point, out Error))
                return false;

            if (!Own.TryChange(json => {
                                   var points = json["gridConnectionPoints"] as JArray ?? [];
                                   var index  = points.OfType<JObject>().ToList().FindIndex(one => id.Equals(one.Value<String>("@id"), StringComparison.OrdinalIgnoreCase));
                                   if (index < 0)
                                       return $"The grid operator '{Own.Id}' has no grid connection point '{id}'.";
                                   points[index] = point.ToJSON();
                                   return null;
                               },
                               TimeProvider.GetUtcNow(),
                               out _,
                               out Error,
                               out var notSaved))
            {
                Refusal = notSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                return false;
            }

            TryGetGridConnectionPoint(id, out GridConnectionPoint);

            Log.Notice($"'{Who}' changed the grid connection point '{id}' of the grid operator '{Own.Id}'.", "poi", "web");

            return GridConnectionPoint is not null;

        }

        #endregion

        #region TryRemoveGridConnectionPoint(Id, Who, out Error, out Refusal)

        /// <summary>
        /// Remove a grid connection point - not while a local controller is
        /// behind it: the pool it controls is still connected there.
        /// </summary>
        public Boolean TryRemoveGridConnectionPoint(String                            Id,
                                                    String                            Who,
                                                    [NotNullWhen(false)] out String?  Error,
                                                    out ChangeRefusal                 Refusal)
        {

            Refusal = ChangeRefusal.Invalid;

            if (!TryGetGridConnectionPoint(Id, out var existing))
            {
                Error    = $"The grid operator '{Own.Id}' has no grid connection point '{Id}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            var id      = existing.Id.ToString();
            var behind  = LocalControllersAt(id);

            if (behind.Count > 0)
            {
                Error    = $"The local controller(s) {String.Join(", ", behind.Select(login => $"'{login.VENName}'"))} are behind the grid connection point '{id}': " +
                            "remove them first, or move their pools to another point.";
                Refusal  = ChangeRefusal.Conflict;
                return false;
            }

            if (!Own.TryChange(json => {
                                   if (json["gridConnectionPoints"] is JArray points)
                                       foreach (var one in points.OfType<JObject>().Where(one => id.Equals(one.Value<String>("@id"), StringComparison.OrdinalIgnoreCase)).ToArray())
                                           one.Remove();
                                   return null;
                               },
                               TimeProvider.GetUtcNow(),
                               out _,
                               out Error,
                               out var notSaved))
            {
                Refusal = notSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                return false;
            }

            Log.Notice($"'{Who}' removed the grid connection point '{id}' of the grid operator '{Own.Id}'.", "poi", "web");

            return true;

        }

        #endregion


        #region (private static) TryParsePoint(JSON, out Point, out Error)

        /// <summary>
        /// A grid connection point this grid operator maintains itself, as POI 3 writes one.
        /// </summary>
        private static Boolean TryParsePoint(JObject                                         JSON,
                                             [NotNullWhen(true)]  out GridConnectionPoint?  Point,
                                             [NotNullWhen(false)] out String?               Error)
        {

            Point = null;

            var json = (JObject) JSON.DeepClone();

            // What the JSON API adds of its own, read back from a page that
            // sends what it was given: never a part of the point.
            json.Remove("localControllers");
            json.Remove("limits");

            if (json[POIRoamingNetwork.MaintainedByProperty] is JToken maintainedBy && maintainedBy.Type != JTokenType.Null)
            {
                Error = "A grid connection point kept here is maintained by the grid operator itself: it has no 'maintainedBy'.";
                return false;
            }

            if (json["@id"] is not JValue { Type: JTokenType.String } id || String.IsNullOrWhiteSpace(id.Value<String>()))
            {
                Error = "A grid connection point needs an identification, '@id'.";
                return false;
            }

            json["@id"] = id.Value<String>()!.Trim();

            if (!GridConnectionPoint.TryParse(json, out Point, out Error))
                return false;

            return true;

        }

        #endregion

    }

}
