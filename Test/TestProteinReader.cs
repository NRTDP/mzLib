﻿// Copyright 2012, 2013, 2014 Derek J. Bailey
// Modified work Copyright 2016 Stefan Solntsev
//
// This file (TestRange.cs) is part of MassSpectrometry.Tests.
//
// MassSpectrometry.Tests is free software: you can redistribute it and/or modify it
// under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// MassSpectrometry.Tests is distributed in the hope that it will be useful, but WITHOUT
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
// FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public
// License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with MassSpectrometry.Tests. If not, see <http://www.gnu.org/licenses/>.

using NUnit.Framework;
using Proteomics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UsefulProteomicsDatabases;

namespace Test
{
    [TestFixture]
    public sealed class TestProteinReader
    {

        #region Public Methods

        [Test]
        public void XmlTest()
        {
            var nice = new List<Modification>
            {
                new ModificationWithLocation("fayk",null, null,ModificationSites.A,null,  null)
            };

            Dictionary<string, Modification> un;
            var ok = ProteinDbLoader.LoadProteinXML(Path.Combine(TestContext.CurrentContext.TestDirectory, @"xml.xml"), true, nice, false, new List<string> { "Ensembl" }, out un);

            Assert.AreEqual('M', ok[0][0]);
            Assert.AreEqual('M', ok[1][0]);

            Assert.AreEqual("P62805|H4_HUMAN|Histone H4", ok[0].FullDescription);
            Assert.AreEqual("DECOY_P62805|H4_HUMAN|Histone H4", ok[1].FullDescription);
            Assert.AreEqual("ENST00000244537", ok[0].DatabaseReferences.First().Id);
            Assert.AreEqual("protein sequence ID", ok[0].DatabaseReferences.First().Properties.First().Item1);
            Assert.AreEqual("ENSP00000244537", ok[0].DatabaseReferences.First().Properties.First().Item2);
            Assert.AreEqual(42, ok[0].GeneNames.Count());
            Assert.AreEqual(14, ok[0].GeneNames.Where(t => t.Item1 == "primary").Count());
            Assert.AreEqual("HIST1H4A", ok[0].GeneNames.Where(t => t.Item1 == "primary").First().Item2);
            Assert.AreEqual(23, ok[0].DatabaseReferences.Count());
        }

        [Test]
        public void XmlTest_2entry()
        {
            var nice = new List<Modification>
            {
                new ModificationWithLocation("fayk",null, null,ModificationSites.A,null,  null)
            };

            Dictionary<string, Modification> un;
            var ok = ProteinDbLoader.LoadProteinXML(Path.Combine(TestContext.CurrentContext.TestDirectory, @"xml2.xml"), true, nice, false, new List<string> { "EnsemblFungi" }, out un);

            Assert.True(ok.All(p => p.ProteolysisProducts.All(d => d.OneBasedBeginPosition == null || d.OneBasedBeginPosition > 0)));

            Assert.True(ok.All(p => p.ProteolysisProducts.All(d => d.OneBasedEndPosition == null || d.OneBasedEndPosition <= p.Length)));

            Assert.False(ok.All(p => p.BaseSequence.Contains(" ")));
            Assert.False(ok.All(p => p.BaseSequence.Contains("\t")));
            Assert.False(ok.All(p => p.BaseSequence.Contains("\n")));

            //GoTerm checks
            List<Protein> targets = ok.Where(p => !p.IsDecoy).ToList();
            Assert.AreEqual(2, targets.Count);
            Assert.AreEqual(1, targets[0].DatabaseReferences.Count());
            Assert.AreEqual(1, targets[1].DatabaseReferences.Count());
        }

        [Test]
        public void XmlGzTest()
        {
            var nice = new List<Modification>
            {
                new ModificationWithLocation("fayk",null, null,ModificationSites.A,null,  null)
            };

            Dictionary<string, Modification> un;
            var ok = ProteinDbLoader.LoadProteinXML(Path.Combine(TestContext.CurrentContext.TestDirectory, @"xml.xml.gz"), true, nice, false, new List<string> { "Ensembl" }, out un);

            Assert.AreEqual('M', ok[0][0]);
            Assert.AreEqual('M', ok[1][0]);

            Assert.AreEqual("P62805|H4_HUMAN|Histone H4", ok[0].FullDescription);
            Assert.AreEqual("DECOY_P62805|H4_HUMAN|Histone H4", ok[1].FullDescription);
            Assert.AreEqual("ENST00000244537", ok[0].DatabaseReferences.First().Id);
            Assert.AreEqual("protein sequence ID", ok[0].DatabaseReferences.First().Properties.First().Item1);
            Assert.AreEqual("ENSP00000244537", ok[0].DatabaseReferences.First().Properties.First().Item2);
            Assert.AreEqual(42, ok[0].GeneNames.Count());
            Assert.AreEqual(14, ok[0].GeneNames.Where(t => t.Item1 == "primary").Count());
            Assert.AreEqual("HIST1H4A", ok[0].GeneNames.Where(t => t.Item1 == "primary").First().Item2);
            Assert.AreEqual(23, ok[0].DatabaseReferences.Count());
        }

        [Test]
        public void XmlFunkySequenceTest()
        {
            var nice = new List<Modification>
            {
                new ModificationWithLocation("fayk",null, null,ModificationSites.A,null,  null)
            };

            Dictionary<string, Modification> un;
            var ok = ProteinDbLoader.LoadProteinXML(Path.Combine(TestContext.CurrentContext.TestDirectory, @"fake_h4.xml"), true, nice, false, null, out un);

            Assert.AreEqual('S', ok[0][0]);
            Assert.AreEqual('G', ok[1][0]);
        }

        [Test]
        public void XmlModifiedStartTest()
        {
            var nice = new List<Modification>
            {
                new ModificationWithLocation("fayk",null, null,ModificationSites.A,null,  null)
            };

            Dictionary<string, Modification> un;
            var ok = ProteinDbLoader.LoadProteinXML(Path.Combine(TestContext.CurrentContext.TestDirectory, @"modified_start.xml"), true, nice, false, null, out un);

            Assert.AreEqual('M', ok[0][0]);
            Assert.AreEqual('M', ok[1][0]);
            Assert.AreEqual(1, ok[1].OneBasedPossibleLocalizedModifications[1].Count);
        }

        [Test]
        public void FastaTest()
        {
            List<Protein> prots = ProteinDbLoader.LoadProteinFasta(Path.Combine(TestContext.CurrentContext.TestDirectory, @"fasta.fasta"), true, false, ProteinDbLoader.uniprot_accession_expression, ProteinDbLoader.uniprot_fullName_expression, ProteinDbLoader.uniprot_accession_expression, ProteinDbLoader.uniprot_gene_expression);
            Assert.AreEqual("HIST1H4A", prots.First().GeneNames.First().Item2);
        }

        [Test]
        public void load_fasta_handle_tooHigh_indices()
        {
            ProteinDbLoader.LoadProteinFasta(Path.Combine(TestContext.CurrentContext.TestDirectory, @"bad.fasta"), true, false, ProteinDbLoader.uniprot_accession_expression, ProteinDbLoader.uniprot_fullName_expression, ProteinDbLoader.uniprot_accession_expression, ProteinDbLoader.uniprot_gene_expression);
        }

        #endregion Public Methods

    }
}