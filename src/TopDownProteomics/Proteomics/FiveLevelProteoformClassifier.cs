﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using TopDownProteomics.ProForma;

namespace TopDownProteomics.Proteomics
{
    /// <summary>
    /// This static class contains methods for classifying proteoform identifications as defined in the following publication:
    /// Smith, L.M., Thomas, P.M., Shortreed, M.R. et al. A five-level classification system for proteoform identifications. 
    /// Nat Methods 16, 939–940 (2019). https://doi.org/10.1038/s41592-019-0573-x
    /// </summary>
    public static class FiveLevelProteoformClassifier
    {
        private static ProFormaParser Parser = new ProFormaParser();


        /// <summary>
        /// Determine 5-level proteoform classification from ProForma
        /// </summary>
        /// <param name="proFormaString">ProForma string </param>
        /// <param name="genes">List of genes for this proForma </param>
        /// <returns></returns>
        public static string ClassifyProForma(string proFormaString, List<string> genes)
        {
            ProFormaTerm parsedProteoform = Parser.ParseString(proFormaString);

            bool ptmLocalized = ProFormaHasLocalizedPTMs(parsedProteoform);
            bool ptmIdentified = ProFormaHasIdentifiedPTMs(parsedProteoform);
            bool sequenceIdentified = ProFormaHasSequenceIdentified(parsedProteoform);
            bool geneIdentified = genes.Count == 1;
            return GetProteoformClassification(ptmLocalized, ptmIdentified, sequenceIdentified, geneIdentified);
        }

        /// <summary>
        /// Determine if proteoform has all of its PTMs localized
        /// </summary>
        /// <param name="proteoform"></param>
        /// <returns></returns>
        private static bool ProFormaHasLocalizedPTMs(ProFormaTerm proteoform)
        {
            //check unlocalized tags 
            if (proteoform.UnlocalizedTags == null || proteoform.UnlocalizedTags.Count == 0)
            {
                //check tag groups
                if (proteoform.TagGroups != null && proteoform.TagGroups.Count != 0)
                {
                    foreach (ProFormaTagGroup group in proteoform.TagGroups)
                    {
                        if (group.Members != null && group.Members.Count > 1)
                        {
                            return false;
                        }
                    }
                }
                //check tags
                if (proteoform.Tags != null) 
                {
                    foreach (ProFormaTag tag in proteoform.Tags)
                    {
                        if (tag.ZeroBasedStartIndex != tag.ZeroBasedEndIndex)
                        {
                            return false;
                        }
                    }
                }
                //check labile (inherently unlocalized)
                if(proteoform.LabileDescriptors!=null && proteoform.LabileDescriptors.Count!=0)
                {
                    return false;
                }
                //don't need to check N- or C-term, those are localized
                return true;
            }
            return false;
        }

        /// <summary>
        /// Determine if proteoform has all of its PTMs identified
        /// </summary>
        /// <param name="proteoform"></param>
        /// <returns></returns>
        private static bool ProFormaHasIdentifiedPTMs(ProFormaTerm proteoform)
        {
            //if we observed some tags, check that they have names and/or formulas (identified) instead of just a mass shift (not identified)
            if (proteoform.Tags!=null)
            {
                foreach(var tag in proteoform.Tags)
                {
                    if(AmbiguousPtmFromDescriptor(tag.Descriptors))
                    {
                        return false;
                    }
                }
            }
            if(proteoform.UnlocalizedTags!=null)
            {
                foreach (var tag in proteoform.UnlocalizedTags)
                {
                    if (AmbiguousPtmFromDescriptor(tag.Descriptors))
                    {
                        return false;
                    }
                }
            }
            if(proteoform.TagGroups!=null)
            {
                foreach(var tag in proteoform.TagGroups)
                {
                    if(AmbiguousPtmFromKey(tag.Key))
                    {
                        return false;
                    }
                }
            }
            if (proteoform.NTerminalDescriptors != null && AmbiguousPtmFromDescriptor(proteoform.NTerminalDescriptors))
            {
                return false;
            }
            if (proteoform.CTerminalDescriptors != null && AmbiguousPtmFromDescriptor(proteoform.CTerminalDescriptors))
            {
                return false;
            }
            if (proteoform.LabileDescriptors != null && AmbiguousPtmFromDescriptor(proteoform.LabileDescriptors))
            {
                return false;
            }
            //All PTMs had an ID (or there were no PTMs)
            return true;
        }

        /// <summary>
        /// Given a PTM descriptor, is the PTM unknown (i.e. not have an ID)?
        /// </summary>
        /// <param name="descriptor"></param>
        /// <returns></returns>
        private static bool AmbiguousPtmFromDescriptor(IList<ProFormaDescriptor>? descriptor)
        {
            if (descriptor == null)
            {
                return false;
            }
            else if (descriptor.Count == 1)
            {
                return AmbiguousPtmFromKey(descriptor[0].Key);
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Given a PTM key, is the PTM unknown (i.e. not have an ID)?
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static bool AmbiguousPtmFromKey(ProFormaKey key)
        {
            return (key.Equals(ProFormaKey.Mass) || key.Equals(ProFormaKey.None));
        }


        /// <summary>
        /// Determine if proteoform has its entire sequence identified
        /// </summary>
        /// <param name="proteoform"></param>
        /// <returns></returns>
        private static bool ProFormaHasSequenceIdentified(ProFormaTerm proteoform)
        {
            //easier to check if the sequence is ambiguous and then reverse the bool to find if the sequence is not ambiguous.
            return !((proteoform.AmbiguousAASequences!=null && proteoform.AmbiguousAASequences.Count!=0) ||
                proteoform.Sequence.Contains('X') ||
                proteoform.Sequence.Contains('J') ||
                proteoform.Sequence.Contains('B') ||
                proteoform.Sequence.Contains('Z'));
        }

        /// <summary>
        /// Determine 5-level proteoform classification from pipe-format
        /// All input strings are delimited with "|"
        /// PTMs are annotated with []
        /// </summary>
        /// <param name="fullSequenceString">All possible sequences (with modifications) for this PrSM</param>
        /// <param name="geneString">All possible genes for this PrSM</param>
        /// <returns></returns>
        public static string ClassifyPrSM(string fullSequenceString, string geneString)
        {
            //separate delimited input
            string[] sequences = fullSequenceString.Split('|');
            string[] genes = geneString.Split('|');


            //determine sequence ambiguity
            string firstBaseSequence = GetBaseSequenceFromFullSequence(sequences[0]).ToUpper(); //get first sequence with modifications removed
            bool sequenceIdentified = !SequenceContainsUnknownAminoAcids(firstBaseSequence); //check if there are any ambiguous amino acids (i.e. B, J, X, Z)
            //for every other sequence reported
            if (sequenceIdentified) //if there weren't any unknown amino acids reported.
            {
                for (int i = 1; i < sequences.Length; i++)
                {
                    //if the unmodified sequences don't match, then there's sequence ambiguity
                    if (!firstBaseSequence.Equals(GetBaseSequenceFromFullSequence(sequences[i]).ToUpper()))
                    {
                        sequenceIdentified = false;
                        break;
                    }
                }
            }


            //determine PTM localization and identification
            List<(int index, string ptm)> firstPTMsSortedByIndex = GetPTMs(sequences[0]); //get ptms from the first sequence reported
            List<string> firstPTMsSortedByPTM = firstPTMsSortedByIndex.Select(x => x.ptm).OrderBy(x => x).ToList(); //sort ptms alphabetically
            //check if there are unknown mass shifts
            bool ptmsIdentified = !PtmsContainUnknownMassShifts(firstPTMsSortedByPTM);
            bool ptmsLocalized = true; //assume these are localized unless we determine otherwise
            //for every other sequence reported
            for (int seqIndex = 1; seqIndex < sequences.Length; seqIndex++)
            {
                List<(int index, string ptm)> currentPTMsSortedByIndex = GetPTMs(sequences[seqIndex]); //get ptms from this sequence
                List<string> currentPTMsSortedByPTM = currentPTMsSortedByIndex.Select(x => x.ptm).OrderBy(x => x).ToList(); //sort ptms alphabetically

                //are number of PTMs the same?
                if (firstPTMsSortedByIndex.Count == currentPTMsSortedByIndex.Count)
                {
                    //check localization (are indexes conserved?)
                    for (int i = 0; i < firstPTMsSortedByIndex.Count; i++)
                    {
                        if (firstPTMsSortedByIndex[i].index != currentPTMsSortedByIndex[i].index)
                        {
                            ptmsLocalized = false;
                            break;
                        }
                    }
                    //check PTMs
                    for (int i = 0; i < firstPTMsSortedByPTM.Count; i++)
                    {
                        if (!firstPTMsSortedByPTM[i].Equals(currentPTMsSortedByPTM[i]))
                        {
                            ptmsIdentified = false;
                            break;
                        }
                    }
                }
                else
                {
                    ptmsIdentified = false;
                    ptmsLocalized = false;
                }
            }


            //determine gene ambiguity
            bool geneIdentified = genes.Length == 1;


            return GetProteoformClassification(ptmsLocalized, ptmsIdentified, sequenceIdentified, geneIdentified);
        }

        /// <summary>
        /// Determine proteoform level between 1 (know everything) and 5 (only know the mass)
        /// as defined in the publication:
        /// Smith, L.M., Thomas, P.M., Shortreed, M.R. et al. A five-level classification system for proteoform identifications. 
        /// Nat Methods 16, 939–940 (2019). https://doi.org/10.1038/s41592-019-0573-x
        /// </summary>
        /// <param name="ptmLocalized">Is the PTM localized?</param>
        /// <param name="ptmIdentified">Do we know what the PTM is, or is it ambiguous (or an unknown mass shift?)</param>
        /// <param name="sequenceIdentified">Do we know the proteoform sequence, or is it ambiguous?</param>
        /// <param name="geneIdentified">Do we know which gene produced this proteoform?</param>
        /// <returns></returns>
        public static string GetProteoformClassification(bool ptmLocalized, bool ptmIdentified, bool sequenceIdentified, bool geneIdentified)
        {
            int sum = Convert.ToInt16(ptmLocalized) + Convert.ToInt16(ptmIdentified) + Convert.ToInt16(sequenceIdentified) + Convert.ToInt16(geneIdentified);
            if (sum == 3) //level 2, but is it A, B, C, or D?
            {
                if (!ptmLocalized)
                {
                    return "2A";
                }
                else if (!ptmIdentified)
                {
                    return "2B";
                }
                else if (!sequenceIdentified)
                {
                    return "2C";
                }
                else //if (!geneIdentified)
                {
                    return "2D";
                }
            }
            else
            {
                return (5 - sum).ToString();
            }
        }

        /// <summary>
        /// Provided with an unmodified sequence, return if it contains ambiguous amino acids such as:
        /// B: Aspartic acid or Asparagine
        /// J: Leucine or Isoleucine
        /// X: Any amino acid
        /// Z: Glutamic acid or Glutamine
        /// </summary>
        /// <param name="baseSequence"></param>
        /// <returns></returns>
        private static bool SequenceContainsUnknownAminoAcids(string baseSequence)
        {
            char[] ambiguousAminoAcids = new char[] { 'B', 'J', 'X', 'Z' };
            foreach (char aa in ambiguousAminoAcids)
            {
                if (baseSequence.Contains(aa))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Given a proteoform sequence (contains ptms), returns a list of all ptms and their one based index in order from N-terminus to C-terminus
        /// </summary>
        /// <param name="fullSequence"></param>
        /// <returns></returns>
        private static List<(int, string)> GetPTMs(string fullSequence)
        {
            List<(int, string)> ptmsToReturn = new List<(int, string)>();
            StringBuilder currentPTM = new StringBuilder();
            int currentIndex = 0;
            int numLeftBrackets = 0; //PTMs are annotated with brackets. This object keeps track of how many brackets deep we are

            //iterate through the sequence
            foreach (char c in fullSequence)
            {
                //if we found a right bracket
                if (c == ']')
                {
                    //record that we're stepping out of brackets
                    numLeftBrackets--;
                    //if we've finished the ptm
                    if (numLeftBrackets == 0)
                    {
                        //Add the ptm and clear the record
                        currentIndex--; //move back an index because we added one when we entered the bracket 
                        ptmsToReturn.Add((currentIndex, currentPTM.ToString()));
                        currentPTM.Clear();
                    }
                }
                else //if not a right bracket...
                {
                    //if we're already in a PTM, record it
                    if (numLeftBrackets > 0)
                    {
                        currentPTM.Append(c);
                    }
                    else //we're not in a PTM, so update where we are in the proteoform
                    {
                        currentIndex++; //this operation occurs when entering a PTM, so we need to substract when exiting the PTM
                    }
                    //if we're entering a PTM or a nested bracket, record it
                    if (c == '[')
                    {
                        numLeftBrackets++;
                    }
                }
            }

            return ptmsToReturn;
        }

        /// <summary>
        /// See if any of the reported PTMs are mass shifts, (e.g. [+15.99] or [-17.99]) or contain "?"
        /// </summary>
        /// <param name="ptms"></param>
        /// <returns></returns>
        private static bool PtmsContainUnknownMassShifts(List<string> ptms)
        {
            foreach (string ptm in ptms)
            {
                if (ptm.Length > 1) //check length is appropriate
                {
                    //remove sign with substring and try to parse into double. If it's a mass, tryparse returns true
                    if (double.TryParse(ptm.Substring(1), out double mass))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Get unmodified proteoform sequence from a modified sequence
        /// PTMs are annotated with brackets []
        /// brackets can be nested
        /// </summary>
        /// <param name="fullSequence">the modified proteoform sequence</param>
        /// <returns></returns>
        private static string GetBaseSequenceFromFullSequence(string fullSequence)
        {
            StringBuilder sb = new StringBuilder();
            int bracketCount = 0;
            foreach (char c in fullSequence)
            {
                if (c == '[')
                {
                    bracketCount++;
                }
                else if (c == ']')
                {
                    bracketCount--;
                }
                else if (bracketCount == 0)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}