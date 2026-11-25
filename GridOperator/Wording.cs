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

using System.Globalization;

using cloud.charging.open.protocols.WWCP.Node.Connections;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// How many of something there are, said as English says it: "1 local
    /// controller", "0 local controllers", "3 changes" - not "(s)".
    /// </summary>
    public static class Wording
    {

        /// <summary>
        /// The given number with the given noun, in the singular for one and
        /// in the plural (NodeWording.Plural) otherwise.
        /// </summary>
        /// <param name="Count">How many.</param>
        /// <param name="Noun">A noun in the singular.</param>
        public static String Counted(Int64   Count,
                                     String  Noun)

            => $"{Count} {(Count == 1 ? Noun : NodeWording.Plural(Noun))}";

        /// <summary>
        /// The given number - "1.5", whatever the culture - with the given
        /// noun, in the singular for exactly one and in the plural otherwise:
        /// "1 minute", "1.5 minutes".
        /// </summary>
        /// <param name="Count">How many.</param>
        /// <param name="Noun">A noun in the singular.</param>
        public static String Counted(Double  Count,
                                     String  Noun)

            => $"{Count.ToString(CultureInfo.InvariantCulture)} {(Count == 1 ? Noun : NodeWording.Plural(Noun))}";

    }

}
