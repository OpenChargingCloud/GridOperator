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

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// Why a change of the grid operator was not made - each the status code
    /// the JSON API answers it with.
    /// </summary>
    public enum ChangeRefusal
    {

        /// <summary>
        /// What was asked for is wrong in itself: 400.
        /// </summary>
        Invalid,

        /// <summary>
        /// What it is about is not there: 404.
        /// </summary>
        NotFound,

        /// <summary>
        /// It is there already, or something else still needs it: 409.
        /// </summary>
        Conflict,

        /// <summary>
        /// Nothing about the change was wrong, and the file it is kept in could
        /// not be written: 500.
        /// </summary>
        NotSaved,

        /// <summary>
        /// Somebody else had to say yes, and said no or could not be asked: 502.
        /// </summary>
        Upstream

    }

}
