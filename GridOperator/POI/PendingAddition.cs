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

using cloud.charging.open.protocols.WWCP.POI;

using POIGridOperator = cloud.charging.open.protocols.WWCP.POI.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.POI
{

    /// <summary>
    /// A change set waiting at the roaming network that adds this grid
    /// operator - made and signed by the operator of the roaming network - and
    /// waits for the signature of the identity key it enters: the proof that
    /// the grid operator holds it (SPECIFICATION.md of WWCP POI, section 4.1).
    /// </summary>
    /// <remarks>
    /// The identity key signs nothing but that: a change set that changes
    /// anything but this grid operator, or gives it a key that is not its own,
    /// is not signed, whoever made it - with its identity key it would vouch
    /// for what it does not hold or does not say.
    /// </remarks>
    public static class PendingAddition
    {

        #region Concerns(ChangeSet, Own)

        /// <summary>
        /// Whether the given change set changes the given grid operator at all.
        /// </summary>
        public static Boolean Concerns(RoamingNetworkChangeSet  ChangeSet,
                                       OwnGridOperator          Own)

            => ChangeSet.Changes.Any(change => IsOwn(change, Own));

        #endregion

        #region WhyNotToSign(Head, ChangeSet, Own)

        /// <summary>
        /// Why the given grid operator does not sign the given change set with
        /// its identity key - or null, where it is the addition of it, with its
        /// own keys, and nothing else.
        /// </summary>
        /// <param name="Head">The roaming network, as its head is now.</param>
        /// <param name="ChangeSet">The change set waiting.</param>
        /// <param name="Own">The grid operator this node is.</param>
        public static String? WhyNotToSign(RoamingNetwork           Head,
                                           RoamingNetworkChangeSet  ChangeSet,
                                           OwnGridOperator          Own)
        {

            if (ChangeSet.RoamingNetworkId != Head.Id)
                return $"It is a change set of the roaming network '{ChangeSet.RoamingNetworkId}', not of '{Head.Id}'.";

            if (ChangeSet.Signatures.Any(signature => signature.KeyId == Own.IdentityKey.Id))
                return $"It is signed with the identity key of the grid operator '{Own.Id}' already.";

            if (ChangeSet.Changes.FirstOrDefault(change => !IsOwn(change, Own)) is RoamingNetworkChange other)
                return $"It changes the {other.EntityType} '{other.EntityId}' as well, which is not the grid operator '{Own.Id}': " +
                        "its identity key vouches for nothing but itself.";

            if (!Head.TryApplyPendingChangeSet(ChangeSet, out var next, out var error))
                return $"The roaming network would not take it: {error}";

            if (!next.GridOperators.TryGet(Own.Id, out var added) || added is null)
                return $"It does not add the grid operator '{Own.Id}'.";

            if (added.IdentityKeys.FirstOrDefault(key => key.Id != Own.IdentityKey.Id) is POIPublicKey foreignIdentity)
                return $"It gives the grid operator '{Own.Id}' the identity key '{foreignIdentity.Id}', which is not its own.";

            if (added.SigningKeys.FirstOrDefault(key => key.Id != Own.SigningKey.Id) is POIPublicKey foreignSigning)
                return $"It gives the grid operator '{Own.Id}' the signing key '{foreignSigning.Id}', which is not its own.";

            if (!added.IdentityKeys.Any(key => key.Id == Own.IdentityKey.Id))
                return $"It does not give the grid operator '{Own.Id}' its identity key, so there is nothing for it to prove.";

            return null;

        }

        #endregion


        #region (private static) IsOwn(Change, Own)

        private static Boolean IsOwn(RoamingNetworkChange  Change,
                                     OwnGridOperator       Own)

            => Change.EntityType == nameof(protocols.WWCP.POI.GridOperator) &&
               Change.EntityId   == Own.Id.ToString();

        #endregion

    }

}
