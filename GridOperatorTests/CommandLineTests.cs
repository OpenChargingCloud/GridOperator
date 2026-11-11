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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.CLI;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;

using cloud.charging.open.GridOperator.CommandLine;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The command line of a grid operator: its own, whichever program runs it,
    /// and what its commands do - the same as the web interface and the JSON
    /// API, a third way of asking for it.
    /// </summary>
    public class CommandLineTests : AGridOperatorTests
    {

        #region (helper) Run(CLI, Command)

        /// <summary>
        /// What the given command line answers to the given command, as one text.
        /// </summary>
        private static async Task<String> Run(NodeCLI CLI, String Command)

            => String.Join("\n", await CLI.Execute(Command));

        #endregion


        #region OverSSHTheGridOperatorHasItsOwnCommands()

        /// <summary>
        /// Whoever signs in over SSH gets the command line of the grid operator,
        /// with its commands, without the program that runs it having to say so.
        /// </summary>
        [Test]
        public async Task OverSSHTheGridOperatorHasItsOwnCommands()
        {

            var grid = GridOperatorFrom();

            Assert.That(grid.CommandLines, Is.Not.Null, "a session over SSH would get the node's command line only");

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = grid.CommandLines!(terminal, CLICaller.Console);

            Assert.That(cli, Is.InstanceOf<GridOperatorCLI>());

            foreach (var command in new[] { "gridOperator", "gridConnectionPoints", "localControllers", "limits", "publish" })
                Assert.That(await Run(cli, command), Does.Not.StartWith("Unknown command"), command);

            Assert.That(await Run(cli, "gridOperator nonsense"), Does.StartWith("Usage: gridOperator"));
            Assert.That(await Run(cli, "limits nonsense"),       Does.StartWith("Usage: limits"));

        }

        #endregion

        #region TheGridOperatorSaysWhoItIs()

        /// <summary>
        /// "gridOperator" says which grid operator this is and its public keys,
        /// "gridOperator party" one line of JSON for the operator of a roaming
        /// network - and never anything private.
        /// </summary>
        [Test]
        public async Task TheGridOperatorSaysWhoItIs()
        {

            var       grid  = GridOperatorFrom();
            using var cli   = new GridOperatorCLI(grid);

            var who   = await Run(cli, "gridOperator");
            var party = await cli.Execute("gridOperator party");

            Assert.Multiple(() => {
                Assert.That(who,                 Does.Contain("'DE*SWJ', Netze Beispielstadt"));
                Assert.That(who,                 Does.Contain("signing key"));
                Assert.That(who,                 Does.Contain("identity key"));
                Assert.That(who,                 Does.Contain("no roaming network"));
                Assert.That(party,               Has.Length.EqualTo(2));
                Assert.That(party[1],            Does.StartWith("{"));
                Assert.That(party[1],            Does.Not.Contain("private").IgnoreCase);
            });

        }

        #endregion

        #region PointsControllersAndLimitsAtTheCommandLine()

        /// <summary>
        /// A grid connection point, a local controller behind it and a limit
        /// for it, added, listed, cancelled and removed at the command line -
        /// and a point with a local controller behind it is not removed.
        /// </summary>
        [Test]
        public async Task PointsControllersAndLimitsAtTheCommandLine()
        {

            var       grid  = GridOperatorFrom();
            using var cli   = new GridOperatorCLI(grid);

            Assert.That(await Run(cli, "gridConnectionPoints"), Does.Contain("no grid connection point yet"));

            #region A point

            await Run(cli, "gridConnectionPoints add GCP-1 LowVoltage 110 Netzanschluss Paradies");

            Assert.That(grid.TryGetGridConnectionPoint("GCP-1", out _), Is.True, await Run(cli, "gridConnectionPoints"));

            var points = await Run(cli, "gridConnectionPoints");
            Assert.That(points, Does.Contain("GCP-1"));
            Assert.That(points, Does.Contain("110 kW"));
            Assert.That(points, Does.Contain("0 local controller(s)"));

            Assert.That(await Run(cli, "gridConnectionPoints GCP-1"),   Does.Contain("\"LowVoltage\""));
            Assert.That(await Run(cli, "gridConnectionPoints GCP-9"),   Does.Contain("has no grid connection point 'GCP-9'"));

            #endregion

            #region A local controller behind it

            var added = await Run(cli, "localControllers add lc-paradies DE*GEF*P1 GCP-1");

            Assert.That(grid.OpenADR.Logins().Select(login => login.VENName), Is.EqualTo(new[] { "lc-paradies" }), added);
            Assert.That(added, Does.Contain("Its secret is said this once"));

            Assert.That(await Run(cli, "gridConnectionPoints"),  Does.Contain("1 local controller(s): lc-paradies"));
            Assert.That(await Run(cli, "localControllers"),      Does.Contain("pool DE*GEF*P1, behind GCP-1, not seen since this start"));

            Assert.That(await Run(cli, "localControllers add lc-x DE*GEF*P9 nowhere"), Does.Not.Contain("may sign in now"));
            Assert.That(grid.OpenADR.Logins(), Has.Count.EqualTo(1));

            #endregion

            #region A limit for it

            Assert.That(await Run(cli, "limits"), Does.Contain("No limit was sent yet"));

            await Run(cli, "limits send GCP-1 import 44 60");

            var limit = grid.OpenADR.Events().Single();

            Assert.Multiple(() => {
                Assert.That(limit.Kind,                     Is.EqualTo("IMPORT_CAPACITY_LIMIT"));
                Assert.That(limit.LimitKW,                  Is.EqualTo(44));
                Assert.That(limit.End - limit.Start,        Is.EqualTo(TimeSpan.FromHours(1)));
                Assert.That(limit.GridConnectionPointId,    Is.EqualTo("GCP-1"));
                Assert.That(limit.VENNames,                 Is.EqualTo(new[] { "lc-paradies" }));
            });

            var limits = await Run(cli, "limits");
            Assert.That(limits, Does.Contain(limit.Id.ToString()));
            Assert.That(limits, Does.Contain("IMPORT_CAPACITY_LIMIT 44 kW"));
            Assert.That(limits, Does.Contain("behind GCP-1: lc-paradies"));

            await Run(cli, $"limits cancel {limit.Id}");
            Assert.That(await Run(cli, "limits"), Does.Not.Contain("active"));

            #endregion

            #region Removed, in the order it has to be

            await Run(cli, "gridConnectionPoints remove GCP-1");
            Assert.That(grid.TryGetGridConnectionPoint("GCP-1", out _), Is.True, "a point with a local controller behind it was removed");

            await Run(cli, "localControllers remove lc-paradies");
            Assert.That(grid.OpenADR.Logins(), Is.Empty);

            await Run(cli, "gridConnectionPoints remove GCP-1");
            Assert.That(grid.TryGetGridConnectionPoint("GCP-1", out _), Is.False);

            #endregion

        }

        #endregion

        #region ANewSecretReplacesTheOldOne()

        /// <summary>
        /// "localControllers secret" says a new secret once, and the old one no
        /// longer signs in.
        /// </summary>
        [Test]
        public async Task ANewSecretReplacesTheOldOne()
        {

            var       grid  = GridOperatorFrom();
            using var cli   = new GridOperatorCLI(grid);

            await Run(cli, "gridConnectionPoints add GCP-1 LowVoltage 110");

            var first   = await cli.Execute("localControllers add lc-paradies DE*GEF*P1 GCP-1");
            var second  = await cli.Execute("localControllers secret lc-paradies");

            Assert.That(String.Join("\n", second), Does.Contain("said this once"));
            Assert.That(second.Except(first).Any(line => line.Contains("secret")), Is.True, "the new secret is the old one");

        }

        #endregion

        #region PublishingWithoutARoamingNetwork()

        /// <summary>
        /// "publish" says that there is nowhere to publish to, rather than
        /// failing, when no roaming network is set.
        /// </summary>
        [Test]
        public async Task PublishingWithoutARoamingNetwork()
        {

            var       grid  = GridOperatorFrom();
            using var cli   = new GridOperatorCLI(grid);

            var preview = await Run(cli, "publish");

            Assert.That(preview, Is.Not.Empty);
            Assert.That(preview, Does.Not.StartWith("Usage"));
            Assert.That(await Run(cli, "publish later"), Does.StartWith("Usage: publish"));

        }

        #endregion

    }

}
