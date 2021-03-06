﻿// Copyright 2012, 2013, 2014 Derek J. Bailey
// Modified work Copyright 2016, 2017 Stefan Solntsev
//
// This file (Mzml.cs) is part of MassSpecFiles.
//
// MassSpecFiles is free software: you can redistribute it and/or modify it
// under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// MassSpecFiles is distributed in the hope that it will be useful, but WITHOUT
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
// FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public
// License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with MassSpecFiles. If not, see <http://www.gnu.org/licenses/>.

using Ionic.Zlib;
using MassSpectrometry;
using MzLibUtil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IO.MzML
{
    public class Mzml : MsDataFile<IMzmlScan>, IMsStaticDataFile<IMzmlScan>
    {

        #region Private Fields

        private const string _zlibCompression = "MS:1000574";
        private const string _64bit = "MS:1000523";
        private const string _32bit = "MS:1000521";
        private const string _filterString = "MS:1000512";
        private const string _centroidSpectrum = "MS:1000127";
        private const string _profileSpectrum = "MS:1000128";
        private const string _peakIntensity = "MS:1000042";
        private const string _totalIonCurrent = "MS:1000285";
        private const string _scanWindowLowerLimit = "MS:1000501";
        private const string _scanWindowUpperLimit = "MS:1000500";
        private const string _msnOrderAccession = "MS:1000511";
        private const string _precursorCharge = "MS:1000041";
        private const string _selectedIonMz = "MS:1000744";
        private const string _isolationWindowTargetMZ = "MS:1000827";
        private const string _isolationWindowLowerOffset = "MS:1000828";
        private const string _isolationWindowUpperOffset = "MS:1000829";
        private const string _retentionTime = "MS:1000016";
        private const string _ionInjectionTime = "MS:1000927";
        private const string _mzArray = "MS:1000514";
        private const string _intensityArray = "MS:1000515";
        private static readonly Regex MZAnalyzerTypeRegex = new Regex(@"^[a-zA-Z]*", RegexOptions.Compiled);

        private static readonly Dictionary<string, Polarity> polarityDictionary = new Dictionary<string, Polarity>
                {
                    {"MS:1000129",Polarity.Negative},
                    {"MS:1000130",Polarity.Positive}
                };

        private static readonly Dictionary<string, MZAnalyzerType> analyzerDictionary = new Dictionary<string, MZAnalyzerType>
            {
                { "ITMS", MZAnalyzerType.IonTrap2D},
                { "TQMS", MZAnalyzerType.Unknown},
                { "SQMS",MZAnalyzerType.Unknown},
                { "TOFMS",MZAnalyzerType.TOF},
                { "FTMS", MZAnalyzerType.Orbitrap},
                { "Sector", MZAnalyzerType.Sector},
                { "MS:1000081",MZAnalyzerType.Quadrupole},
                { "MS:1000291",MZAnalyzerType.IonTrap2D},
                { "MS:1000082",MZAnalyzerType.IonTrap3D},
                { "MS:1000484",MZAnalyzerType.Orbitrap},
                { "MS:1000084",MZAnalyzerType.TOF},
                { "MS:1000079",MZAnalyzerType.FTICR},
                { "MS:1000080",MZAnalyzerType.Sector}
            };

        private static readonly Dictionary<string, DissociationType> dissociationDictionary = new Dictionary<string, DissociationType>
                {
                    { "MS:1000133",DissociationType.CID},
                    { "MS:1001880",DissociationType.ISCID},
                    { "MS:1000422",DissociationType.HCD},
                    { "MS:1000598",DissociationType.ETD},
                    { "MS:1000435",DissociationType.MPD},
                    { "MS:1000599",DissociationType.PQD},
                    { "MS:1000044",DissociationType.Unknown}
                };

        #endregion Private Fields

        #region Private Constructors

        private Mzml(IMzmlScan[] scans) : base(scans)
        {
        }

        #endregion Private Constructors

        #region Public Methods

        public static Mzml LoadAllStaticData(string filePath)
        {
            Generated.mzMLType _mzMLConnection;

            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open))
                {
                    var _indexedmzMLConnection = (Generated.indexedmzML)MzmlMethods.indexedSerializer.Deserialize(fs);
                    _mzMLConnection = _indexedmzMLConnection.mzML;
                }
            }
            catch
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open))
                    _mzMLConnection = (Generated.mzMLType)MzmlMethods.mzmlSerializer.Deserialize(fs);
            }

            var numSpecta = _mzMLConnection.run.spectrumList.spectrum.Length;
            IMzmlScan[] scans = new IMzmlScan[numSpecta];
            Parallel.ForEach(Partitioner.Create(0, numSpecta), fff =>
            {
                for (int i = fff.Item1; i < fff.Item2; i++)
                    scans[i] = GetMsDataOneBasedScanFromConnection(_mzMLConnection, i + 1);
            });
            return new Mzml(scans);
        }

        public override IMzmlScan GetOneBasedScan(int scanNumber)
        {
            return Scans[scanNumber - 1];
        }

        #endregion Public Methods

        #region Private Methods

        private static IMzmlScan GetMsDataOneBasedScanFromConnection(Generated.mzMLType _mzMLConnection, int oneBasedSpectrumNumber)
        {
            double[] masses = null;
            double[] intensities = null;

            foreach (Generated.BinaryDataArrayType binaryData in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].binaryDataArrayList.binaryDataArray)
            {
                bool compressed = false;
                bool mzArray = false;
                bool intensityArray = false;
                bool is32bit = true;
                foreach (Generated.CVParamType cv in binaryData.cvParam)
                {
                    compressed |= cv.accession.Equals(_zlibCompression);
                    is32bit &= !cv.accession.Equals(_64bit);
                    is32bit |= cv.accession.Equals(_32bit);
                    mzArray |= cv.accession.Equals(_mzArray);
                    intensityArray |= cv.accession.Equals(_intensityArray);
                }

                double[] data = ConvertBase64ToDoubles(binaryData.binary, compressed, is32bit);
                if (mzArray)
                    masses = data;

                if (intensityArray)
                    intensities = data;
            }

            var ok = new MzmlMzSpectrum(masses, intensities, false);

            int? msOrder = null;
            bool? isCentroid = null;
            Polarity polarity = Polarity.Unknown;
            double tic = double.NaN;

            foreach (Generated.CVParamType cv in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].cvParam)
            {
                if (cv.accession.Equals(_msnOrderAccession))
                    msOrder = int.Parse(cv.value);
                if (cv.accession.Equals(_centroidSpectrum))
                    isCentroid = true;
                if (cv.accession.Equals(_profileSpectrum))
                    isCentroid = false;
                if (cv.accession.Equals(_totalIonCurrent))
                    tic = double.Parse(cv.value);
                polarityDictionary.TryGetValue(cv.accession, out polarity);
            }

            double rtInMinutes = double.NaN;
            string scanFilter = null;
            double? injectionTime = null;
            if (_mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].scanList.scan[0].cvParam != null)
                foreach (Generated.CVParamType cv in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].scanList.scan[0].cvParam)
                {
                    if (cv.accession.Equals(_retentionTime))
                    {
                        rtInMinutes = double.Parse(cv.value);
                        if (cv.unitName == "second")
                            rtInMinutes /= 60;
                    }
                    if (cv.accession.Equals(_filterString))
                    {
                        scanFilter = cv.value;
                    }
                    if (cv.accession.Equals(_ionInjectionTime))
                    {
                        injectionTime = double.Parse(cv.value);
                    }
                }

            double high = double.NaN;
            double low = double.NaN;

            if (_mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].scanList.scan[0].scanWindowList != null)
                foreach (Generated.CVParamType cv in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].scanList.scan[0].scanWindowList.scanWindow[0].cvParam)
                {
                    if (cv.accession.Equals(_scanWindowLowerLimit))
                        low = double.Parse(cv.value);
                    if (cv.accession.Equals(_scanWindowUpperLimit))
                        high = double.Parse(cv.value);
                }

            if (msOrder.Value == 1)
            {
                return new MzmlScan(oneBasedSpectrumNumber, ok, msOrder.Value, isCentroid.Value, polarity, rtInMinutes, new MzRange(low, high), scanFilter, GetMzAnalyzer(_mzMLConnection, scanFilter), tic, injectionTime);
            }

            double selectedIonMz = double.NaN;
            int? selectedIonCharge = null;
            double? selectedIonIntensity = null;
            foreach (Generated.CVParamType cv in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].precursorList.precursor[0].selectedIonList.selectedIon[0].cvParam)
            {
                if (cv.accession.Equals(_selectedIonMz))
                    selectedIonMz = double.Parse(cv.value);
                if (cv.accession.Equals(_precursorCharge))
                    selectedIonCharge = int.Parse(cv.value);
                if (cv.accession.Equals(_peakIntensity))
                    selectedIonIntensity = double.Parse(cv.value);
            }

            double? isolationMz = null;
            double lowIsolation = double.NaN;
            double highIsolation = double.NaN;
            foreach (Generated.CVParamType cv in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].precursorList.precursor[0].isolationWindow.cvParam)
            {
                if (cv.accession.Equals(_isolationWindowTargetMZ))
                {
                    isolationMz = double.Parse(cv.value);
                }
                if (cv.accession.Equals(_isolationWindowLowerOffset))
                {
                    lowIsolation = double.Parse(cv.value);
                }
                if (cv.accession.Equals(_isolationWindowUpperOffset))
                {
                    highIsolation = double.Parse(cv.value);
                }
            }
            DissociationType dissociationType = DissociationType.Unknown;
            foreach (Generated.CVParamType cv in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].precursorList.precursor[0].activation.cvParam)
            {
                dissociationDictionary.TryGetValue(cv.accession, out dissociationType);
            }
            double? monoisotopicMz = null;
            if (_mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].scanList.scan[0].userParam != null)
                foreach (var userParam in _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].scanList.scan[0].userParam)
                {
                    if (userParam.name.EndsWith("Monoisotopic M/Z:"))
                    {
                        monoisotopicMz = double.Parse(userParam.value);
                    }
                }

            return new MzmlScanWithPrecursor(oneBasedSpectrumNumber,
                ok,
                msOrder.Value,
                isCentroid.Value,
                polarity,
                rtInMinutes,
                new MzRange(low, high),
                scanFilter,
                GetMzAnalyzer(_mzMLConnection, scanFilter),
                tic,
                selectedIonMz,
                selectedIonCharge,
                selectedIonIntensity,
                isolationMz.Value,
                lowIsolation + highIsolation,
                dissociationType,
                GetOneBasedPrecursorScanNumber(_mzMLConnection, oneBasedSpectrumNumber),
                monoisotopicMz,
                injectionTime);
        }

        /// <summary>
        /// Converts a 64-based encoded byte array into an double[]
        /// </summary>
        /// <param name="bytes">the 64-bit encoded byte array</param>
        /// <param name="zlibCompressed">Specifies if the byte array is zlib compressed</param>
        /// <returns>a decompressed, de-encoded double[]</returns>
        private static double[] ConvertBase64ToDoubles(byte[] bytes, bool zlibCompressed = false, bool is32bit = true)
        {
            // Add capability of compressed data
            if (zlibCompressed)
                bytes = ZlibStream.UncompressBuffer(bytes);

            int size = is32bit ? sizeof(float) : sizeof(double);

            int length = bytes.Length / size;
            double[] convertedArray = new double[length];

            for (int i = 0; i < length; i++)
            {
                if (is32bit)
                {
                    convertedArray[i] = BitConverter.ToSingle(bytes, i * size);
                }
                else
                {
                    convertedArray[i] = BitConverter.ToDouble(bytes, i * size);
                }
            }
            return convertedArray;
        }

        private static MZAnalyzerType GetMzAnalyzer(Generated.mzMLType _mzMLConnection, string filter)
        {
            if (filter != null && analyzerDictionary.TryGetValue(MZAnalyzerTypeRegex.Match(filter).Captures[0].Value, out MZAnalyzerType valuee))
                return valuee;

            // Maybe in the beginning of the file, there is a single analyzer?
            // Gets the first analyzer used.

            if (_mzMLConnection.instrumentConfigurationList.instrumentConfiguration != null)
                return analyzerDictionary.TryGetValue(_mzMLConnection.instrumentConfigurationList.instrumentConfiguration[0].cvParam[0].accession, out valuee) ? valuee : MZAnalyzerType.Unknown;
            return MZAnalyzerType.Unknown;
        }

        private static int GetOneBasedPrecursorScanNumber(Generated.mzMLType _mzMLConnection, int oneBasedSpectrumNumber)
        {
            string precursorID = _mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].precursorList.precursor[0].spectrumRef;
            do
            {
                oneBasedSpectrumNumber--;
            } while (!precursorID.Equals(_mzMLConnection.run.spectrumList.spectrum[oneBasedSpectrumNumber - 1].id));
            return oneBasedSpectrumNumber;
        }

        #endregion Private Methods

    }
}