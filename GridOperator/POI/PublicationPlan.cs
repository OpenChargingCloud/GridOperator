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

using cloud.charging.open.protocols.WWCP.POI;

using POIGridOperator = cloud.charging.open.protocols.WWCP.POI.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.POI
{

    /// <summary>
    /// One step of a publication, as the page and the log say it.
    /// </summary>
    /// <param name="Kind">"add", "update", "remove" or "untouched".</param>
    /// <param name="EntityType">"GridOperator" or "GridConnectionPoint".</param>
    /// <param name="EntityId">Which one.</param>
    /// <param name="Property">The property changed, where it is one.</param>
    /// <param name="Text">The step, in a sentence.</param>
    public sealed record PublicationStep(String   Kind,
                                         String   EntityType,
                                         String   EntityId,
                                         String?  Property,
                                         String   Text)
    {

        /// <summary>
        /// The step, as the JSON API says it.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("kind",        Kind),
                    new JProperty("entityType",  EntityType),
                    new JProperty("entityId",    EntityId),
                    new JProperty("property",    Property),
                    new JProperty("text",        Text));

    }


    /// <summary>
    /// What it takes to make the grid operator of a roaming network what this
    /// node says it is: the changes of one change set, each as a step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only what is this grid operator's to sign is changed: its own properties
    /// but its keys, and the grid connection points it maintains itself. A point
    /// of the roaming network that a charging station operator or a parking
    /// operator maintains is left as it is - its maintainer signs its changes -
    /// and said so, as a step that is "untouched"; the grid operator takes it
    /// over by maintaining it here under the same identification, which removes
    /// its "maintainedBy" first.
    /// </para>
    /// <para>
    /// A point the roaming network has, and maintained by nobody else, that is
    /// not here any more is removed; a charging pool that still names it makes
    /// the roaming network refuse the whole change set, which the answer to the
    /// commit then says.
    /// </para>
    /// </remarks>
    public sealed class PublicationPlan
    {

        #region Properties

        /// <summary>
        /// Whether the roaming network has this grid operator at all. Where it
        /// has not, there is nothing to sign: its operator adds it first.
        /// </summary>
        public Boolean                               IsParty    { get; }

        /// <summary>
        /// The changes, in the order they go into the change set.
        /// </summary>
        public IReadOnlyList<RoamingNetworkChange>   Changes    { get; }

        /// <summary>
        /// Every step, the untouched ones too.
        /// </summary>
        public IReadOnlyList<PublicationStep>        Steps      { get; }

        /// <summary>
        /// Whether there is anything to publish.
        /// </summary>
        public Boolean                               IsEmpty
            => Changes.Count == 0;

        #endregion

        #region Constructor(s)

        private PublicationPlan(Boolean                              IsParty,
                                IReadOnlyList<RoamingNetworkChange>  Changes,
                                IReadOnlyList<PublicationStep>       Steps)
        {
            this.IsParty  = IsParty;
            this.Changes  = Changes;
            this.Steps    = Steps;
        }

        #endregion


        #region (static) Of(RoamingNetwork, Ours)

        /// <summary>
        /// What it takes to make the grid operator of the given roaming network
        /// the given one.
        /// </summary>
        /// <param name="RoamingNetwork">The roaming network, as its head is now.</param>
        /// <param name="Ours">The grid operator as this node keeps it.</param>
        public static PublicationPlan Of(RoamingNetwork   RoamingNetwork,
                                         POIGridOperator  Ours)
        {

            if (!RoamingNetwork.GridOperators.TryGet(Ours.Id, out var theirs) || theirs is null)
                return new PublicationPlan(false, [], []);

            var changes  = new List<RoamingNetworkChange>();
            var steps    = new List<PublicationStep>();
            var goId     = Ours.Id.ToString();

            #region Its own properties

            var theirsJSON  = theirs.ToJSON();
            var oursJSON    = Ours.  ToJSON();

            foreach (var property in OwnGridOperator.ChangeableProperties)
            {

                var before  = theirsJSON[property];
                var after   = oursJSON  [property];

                if (JToken.DeepEquals(before, after))
                    continue;

                if (after is null || after.Type == JTokenType.Null)
                {
                    changes.Add(RoamingNetworkChange.RemoveProperty(theirs, property, before));
                    steps.  Add(new ("remove", nameof(POIGridOperator), goId, property, $"'{property}' of the grid operator is removed."));
                }
                else
                {
                    changes.Add(RoamingNetworkChange.UpdateProperty(theirs, property, before, after));
                    steps.  Add(new ("update", nameof(POIGridOperator), goId, property, $"'{property}' of the grid operator is set to {Short(after)}."));
                }

            }

            #endregion

            #region Its grid connection points

            var theirPoints = theirs.GridConnectionPoints.ToDictionary(point => point.Id.ToString(), StringComparer.OrdinalIgnoreCase);
            var ourPoints   = Ours.  GridConnectionPoints.ToDictionary(point => point.Id.ToString(), StringComparer.OrdinalIgnoreCase);

            foreach (var (id, ours) in ourPoints.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {

                if (!theirPoints.TryGetValue(id, out var theirPoint))
                {
                    changes.Add(RoamingNetworkChange.Add(ours, Ours.Id));
                    steps.  Add(new ("add", nameof(GridConnectionPoint), id, null, $"The grid connection point '{id}' is added."));
                    continue;
                }

                var before  = theirPoint.ToJSON();
                var after   = ours.      ToJSON();

                // Taken over first: from then on, its changes are this grid
                // operator's to sign.
                if (theirPoint.MaintainedBy is POIPartyId maintainer && ours.MaintainedBy is null)
                {
                    changes.Add(RoamingNetworkChange.RemoveProperty(theirPoint, RoamingNetwork.MaintainedByProperty, before[RoamingNetwork.MaintainedByProperty]));
                    steps.  Add(new ("update", nameof(GridConnectionPoint), id, RoamingNetwork.MaintainedByProperty,
                                     $"The grid connection point '{id}' is taken over from the {maintainer.EntityType} '{maintainer.EntityId}', which maintained it."));
                    before.Remove(RoamingNetwork.MaintainedByProperty);
                }

                foreach (var property in before.Properties().Select(p => p.Name).
                                         Union (after. Properties().Select(p => p.Name)).
                                         Where (name => name != "@id" && name != RoamingNetwork.MaintainedByProperty).
                                         Order (StringComparer.Ordinal))
                {

                    var was  = before[property];
                    var is_  = after [property];

                    if (JToken.DeepEquals(was, is_))
                        continue;

                    if (is_ is null || is_.Type == JTokenType.Null)
                    {
                        changes.Add(RoamingNetworkChange.RemoveProperty(theirPoint, property, was));
                        steps.  Add(new ("remove", nameof(GridConnectionPoint), id, property, $"'{property}' of the grid connection point '{id}' is removed."));
                    }
                    else
                    {
                        changes.Add(RoamingNetworkChange.UpdateProperty(theirPoint, property, was, is_));
                        steps.  Add(new ("update", nameof(GridConnectionPoint), id, property, $"'{property}' of the grid connection point '{id}' is set to {Short(is_)}."));
                    }

                }

            }

            foreach (var (id, theirPoint) in theirPoints.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {

                if (ourPoints.ContainsKey(id))
                    continue;

                if (theirPoint.MaintainedBy is POIPartyId maintainer)
                {
                    steps.Add(new ("untouched", nameof(GridConnectionPoint), id, null,
                                   $"The grid connection point '{id}' is maintained by the {maintainer.EntityType} '{maintainer.EntityId}', and left as it is. " +
                                   $"Add it here under the same identification to take it over."));
                    continue;
                }

                changes.Add(RoamingNetworkChange.Remove(theirPoint.Id));
                steps.  Add(new ("remove", nameof(GridConnectionPoint), id, null, $"The grid connection point '{id}' is removed: it is not kept here any more."));

            }

            #endregion

            return new PublicationPlan(true, changes, steps);

        }

        #endregion


        #region (private static) Short(Value)

        /// <summary>
        /// A value, short enough for one line.
        /// </summary>
        private static String Short(JToken Value)
        {

            var text = Value.Type == JTokenType.String
                           ? $"'{Value.Value<String>()}'"
                           : Value.ToString(Newtonsoft.Json.Formatting.None);

            return text.Length > 80
                       ? text[..77] + "..."
                       : text;

        }

        #endregion

    }

}
