using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for fuzzy/approximate string matching.
    /// Supports multiple algorithms: Levenshtein, Jaro-Winkler, Soundex, Metaphone.
    /// </summary>
    public class FuzzyMatchingService
    {
        private readonly ILogger<FuzzyMatchingService> _logger;

        // Common nickname mappings
        private static readonly Dictionary<string, HashSet<string>> NicknameMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Robert", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bob", "Rob", "Bobby", "Robbie", "Bert" } },
            { "William", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bill", "Will", "Billy", "Willy", "Liam" } },
            { "Richard", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rick", "Rich", "Dick", "Ricky", "Richie" } },
            { "Michael", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mike", "Mick", "Mickey", "Mikey" } },
            { "James", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jim", "Jimmy", "Jamie", "Jamey" } },
            { "John", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jack", "Johnny", "Jon" } },
            { "Joseph", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Joe", "Joey", "Jos" } },
            { "Thomas", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Tom", "Tommy", "Thom" } },
            { "Charles", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Charlie", "Chuck", "Chas" } },
            { "Christopher", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Chris", "Topher", "Kit" } },
            { "Daniel", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dan", "Danny", "Dannie" } },
            { "Matthew", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Matt", "Matty" } },
            { "Anthony", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Tony", "Ant" } },
            { "David", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dave", "Davey", "Davy" } },
            { "Andrew", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Andy", "Drew", "Andi" } },
            { "Elizabeth", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Liz", "Beth", "Lizzy", "Betty", "Eliza", "Libby" } },
            { "Jennifer", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jen", "Jenny", "Jenna" } },
            { "Katherine", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Kate", "Katie", "Kathy", "Kat", "Kay" } },
            { "Margaret", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Maggie", "Meg", "Peggy", "Marge", "Margie" } },
            { "Patricia", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pat", "Patty", "Tricia", "Trish" } },
            { "Rebecca", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Becca", "Becky", "Reba" } },
            { "Samantha", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sam", "Sammy" } },
            { "Victoria", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vicky", "Vic", "Tori" } },
            { "Benjamin", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ben", "Benny", "Benji" } },
            { "Alexander", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alex", "Al", "Xander", "Lex" } },
            { "Nicholas", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Nick", "Nicky", "Nico" } },
            { "Jonathan", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jon", "Jonny", "Nathan" } },
            { "Timothy", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Tim", "Timmy" } },
            { "Edward", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ed", "Eddie", "Ted", "Teddy", "Ned" } },
            { "Stephen", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Steve", "Stevie" } },
            { "Steven", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Steve", "Stevie" } },
            { "Gregory", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Greg", "Gregg" } },
            { "Joshua", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Josh" } },
            { "Jacob", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jake", "Jay" } },
        };

        public FuzzyMatchingService(ILogger<FuzzyMatchingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Default composite weights for multi-algorithm scoring.
        /// Levenshtein: Good for typos, Jaro-Winkler: Good for names, Soundex: Good for phonetics
        /// </summary>
        public static readonly Dictionary<string, double> DefaultCompositeWeights = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Levenshtein", 0.40 },  // 40% - Catches typos and edits
            { "JaroWinkler", 0.40 },  // 40% - Good for names with prefix matching
            { "Metaphone", 0.20 }     // 20% - Catches phonetic similarities
        };

        /// <summary>
        /// Calculate similarity between two strings using specified algorithm.
        /// Returns value between 0.0 (no match) and 1.0 (exact match).
        /// </summary>
        public double CalculateSimilarity(string source, string target, string algorithm = "Levenshtein")
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                return 0.0;

            // Normalize strings
            source = source.Trim().ToLowerInvariant();
            target = target.Trim().ToLowerInvariant();

            // Exact match is always 1.0
            if (source == target)
                return 1.0;

            // Check nickname match first (returns 0.95 for nickname matches)
            if (AreNicknames(source, target))
                return 0.95;

            return algorithm.ToLowerInvariant() switch
            {
                "levenshtein" => LevenshteinSimilarity(source, target),
                "jarowinkler" => JaroWinklerSimilarity(source, target),
                "soundex" => SoundexSimilarity(source, target),
                "metaphone" => MetaphoneSimilarity(source, target),
                "composite" => CalculateCompositeSimilarity(source, target, DefaultCompositeWeights),
                _ => LevenshteinSimilarity(source, target)
            };
        }

        /// <summary>
        /// Calculate composite similarity using multiple weighted algorithms.
        /// This provides more robust matching by combining different algorithm strengths:
        /// - Levenshtein: Character-level edits (typos)
        /// - Jaro-Winkler: Prefix-weighted (good for names)
        /// - Metaphone: Phonetic similarity (sounds-alike)
        /// </summary>
        public double CalculateCompositeSimilarity(string source, string target, Dictionary<string, double>? weights = null)
        {
            weights ??= DefaultCompositeWeights;

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                return 0.0;

            // Normalize strings
            source = source.Trim().ToLowerInvariant();
            target = target.Trim().ToLowerInvariant();

            // Exact match is always 1.0
            if (source == target)
                return 1.0;

            // Check nickname match first (returns 0.95 for nickname matches)
            if (AreNicknames(source, target))
                return 0.95;

            double totalWeight = 0;
            double weightedSum = 0;
            var scores = new Dictionary<string, double>();

            foreach (var (algorithm, weight) in weights)
            {
                if (weight <= 0) continue;

                double score = algorithm.ToLowerInvariant() switch
                {
                    "levenshtein" => LevenshteinSimilarity(source, target),
                    "jarowinkler" => JaroWinklerSimilarity(source, target),
                    "soundex" => SoundexSimilarity(source, target),
                    "metaphone" => MetaphoneSimilarity(source, target),
                    _ => 0
                };

                scores[algorithm] = score;
                weightedSum += score * weight;
                totalWeight += weight;
            }

            double compositeScore = totalWeight > 0 ? weightedSum / totalWeight : 0;

            _logger.LogDebug(
                "Composite match '{Source}' vs '{Target}': {CompositeScore:P0} " +
                "(Lev: {Lev:P0}, JW: {JW:P0}, Meta: {Meta:P0})",
                source, target, compositeScore,
                scores.GetValueOrDefault("Levenshtein"),
                scores.GetValueOrDefault("JaroWinkler"),
                scores.GetValueOrDefault("Metaphone"));

            return compositeScore;
        }

        /// <summary>
        /// Get detailed breakdown of composite scoring for UI display.
        /// </summary>
        public CompositeMatchResult GetCompositeMatchDetails(string source, string target, Dictionary<string, double>? weights = null)
        {
            weights ??= DefaultCompositeWeights;

            var result = new CompositeMatchResult
            {
                Source = source,
                Target = target,
                IsExactMatch = source.Equals(target, StringComparison.OrdinalIgnoreCase),
                IsNicknameMatch = AreNicknames(source, target)
            };

            if (result.IsExactMatch)
            {
                result.CompositeScore = 1.0;
                return result;
            }

            if (result.IsNicknameMatch)
            {
                result.CompositeScore = 0.95;
                result.MatchReason = "Nickname match";
                return result;
            }

            // Normalize for comparison
            var s = source.Trim().ToLowerInvariant();
            var t = target.Trim().ToLowerInvariant();

            double totalWeight = 0;
            double weightedSum = 0;

            foreach (var (algorithm, weight) in weights)
            {
                if (weight <= 0) continue;

                double score = algorithm.ToLowerInvariant() switch
                {
                    "levenshtein" => LevenshteinSimilarity(s, t),
                    "jarowinkler" => JaroWinklerSimilarity(s, t),
                    "soundex" => SoundexSimilarity(s, t),
                    "metaphone" => MetaphoneSimilarity(s, t),
                    _ => 0
                };

                result.AlgorithmScores[algorithm] = new AlgorithmScore
                {
                    Algorithm = algorithm,
                    Score = score,
                    Weight = weight,
                    WeightedScore = score * weight
                };

                weightedSum += score * weight;
                totalWeight += weight;
            }

            result.CompositeScore = totalWeight > 0 ? weightedSum / totalWeight : 0;

            // Determine match reason
            var bestAlgo = result.AlgorithmScores
                .OrderByDescending(x => x.Value.Score)
                .FirstOrDefault();

            if (bestAlgo.Value?.Score >= 0.8)
            {
                result.MatchReason = bestAlgo.Key switch
                {
                    "Levenshtein" => "Similar spelling (minor typos)",
                    "JaroWinkler" => "Similar name pattern",
                    "Metaphone" => "Sounds similar",
                    "Soundex" => "Sounds alike",
                    _ => "Multiple algorithm match"
                };
            }

            return result;
        }

        /// <summary>
        /// Check if two strings are known nicknames of each other.
        /// </summary>
        public bool AreNicknames(string name1, string name2)
        {
            name1 = name1.Trim();
            name2 = name2.Trim();

            // Check if name1 is a nickname of name2's formal name
            foreach (var (formalName, nicknames) in NicknameMappings)
            {
                if (formalName.Equals(name1, StringComparison.OrdinalIgnoreCase) && nicknames.Contains(name2))
                    return true;
                if (formalName.Equals(name2, StringComparison.OrdinalIgnoreCase) && nicknames.Contains(name1))
                    return true;
                if (nicknames.Contains(name1) && nicknames.Contains(name2))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Levenshtein distance-based similarity (edit distance).
        /// Good for typos and character transpositions.
        /// </summary>
        public double LevenshteinSimilarity(string source, string target)
        {
            int distance = LevenshteinDistance(source, target);
            int maxLength = Math.Max(source.Length, target.Length);

            if (maxLength == 0) return 1.0;

            return 1.0 - ((double)distance / maxLength);
        }

        /// <summary>
        /// Calculate Levenshtein edit distance between two strings.
        /// </summary>
        public int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target)) return source.Length;

            int[,] d = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= target.Length; j++)
                d[0, j] = j;

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[source.Length, target.Length];
        }

        /// <summary>
        /// Jaro-Winkler similarity - good for short strings like names.
        /// Gives higher scores to strings that match from the beginning.
        /// </summary>
        public double JaroWinklerSimilarity(string source, string target)
        {
            double jaroSim = JaroSimilarity(source, target);

            // Calculate common prefix (up to 4 characters)
            int prefixLength = 0;
            for (int i = 0; i < Math.Min(4, Math.Min(source.Length, target.Length)); i++)
            {
                if (source[i] == target[i])
                    prefixLength++;
                else
                    break;
            }

            // Jaro-Winkler with scaling factor 0.1
            return jaroSim + (prefixLength * 0.1 * (1 - jaroSim));
        }

        private double JaroSimilarity(string source, string target)
        {
            if (source == target) return 1.0;

            int sourceLen = source.Length;
            int targetLen = target.Length;

            if (sourceLen == 0 || targetLen == 0) return 0.0;

            int matchDistance = Math.Max(sourceLen, targetLen) / 2 - 1;
            if (matchDistance < 0) matchDistance = 0;

            bool[] sourceMatches = new bool[sourceLen];
            bool[] targetMatches = new bool[targetLen];

            int matches = 0;
            int transpositions = 0;

            // Find matches
            for (int i = 0; i < sourceLen; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance + 1, targetLen);

                for (int j = start; j < end; j++)
                {
                    if (targetMatches[j] || source[i] != target[j])
                        continue;

                    sourceMatches[i] = true;
                    targetMatches[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0.0;

            // Count transpositions
            int k = 0;
            for (int i = 0; i < sourceLen; i++)
            {
                if (!sourceMatches[i]) continue;

                while (!targetMatches[k]) k++;

                if (source[i] != target[k]) transpositions++;
                k++;
            }

            return ((double)matches / sourceLen +
                    (double)matches / targetLen +
                    (double)(matches - transpositions / 2) / matches) / 3.0;
        }

        /// <summary>
        /// Soundex similarity - phonetic algorithm for English names.
        /// Returns 1.0 if soundex codes match, 0.0 otherwise.
        /// </summary>
        public double SoundexSimilarity(string source, string target)
        {
            string sourceSoundex = Soundex(source);
            string targetSoundex = Soundex(target);

            return sourceSoundex == targetSoundex ? 1.0 : 0.0;
        }

        /// <summary>
        /// Generate Soundex code for a string.
        /// </summary>
        public string Soundex(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "0000";

            input = input.ToUpperInvariant();

            // Keep first letter
            char firstLetter = input[0];

            // Map letters to soundex digits
            var soundexMap = new Dictionary<char, char>
            {
                {'B', '1'}, {'F', '1'}, {'P', '1'}, {'V', '1'},
                {'C', '2'}, {'G', '2'}, {'J', '2'}, {'K', '2'}, {'Q', '2'}, {'S', '2'}, {'X', '2'}, {'Z', '2'},
                {'D', '3'}, {'T', '3'},
                {'L', '4'},
                {'M', '5'}, {'N', '5'},
                {'R', '6'}
            };

            var result = new List<char> { firstLetter };
            char lastCode = soundexMap.GetValueOrDefault(firstLetter, '0');

            for (int i = 1; i < input.Length && result.Count < 4; i++)
            {
                char c = input[i];
                if (soundexMap.TryGetValue(c, out char code) && code != lastCode)
                {
                    result.Add(code);
                    lastCode = code;
                }
                else if (!"AEIOUYHW".Contains(c))
                {
                    lastCode = '0';
                }
            }

            // Pad with zeros
            while (result.Count < 4)
                result.Add('0');

            return new string(result.ToArray());
        }

        /// <summary>
        /// Metaphone similarity - improved phonetic algorithm.
        /// Returns 1.0 if metaphone codes match, partial match score otherwise.
        /// </summary>
        public double MetaphoneSimilarity(string source, string target)
        {
            string sourceMetaphone = Metaphone(source);
            string targetMetaphone = Metaphone(target);

            if (sourceMetaphone == targetMetaphone)
                return 1.0;

            // Use Levenshtein on metaphone codes for partial matching
            return LevenshteinSimilarity(sourceMetaphone, targetMetaphone);
        }

        /// <summary>
        /// Generate Metaphone code for a string (simplified version).
        /// </summary>
        public string Metaphone(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = input.ToUpperInvariant();
            var result = new List<char>();

            int i = 0;

            // Handle special first letter cases
            if (input.StartsWith("KN") || input.StartsWith("GN") || input.StartsWith("PN") ||
                input.StartsWith("AE") || input.StartsWith("WR"))
            {
                i = 1;
            }
            else if (input.StartsWith("WH"))
            {
                result.Add('W');
                i = 2;
            }
            else if (input.StartsWith("X"))
            {
                result.Add('S');
                i = 1;
            }

            while (i < input.Length)
            {
                char c = input[i];

                // Skip duplicate consecutive letters
                if (i > 0 && c == input[i - 1])
                {
                    i++;
                    continue;
                }

                switch (c)
                {
                    case 'A': case 'E': case 'I': case 'O': case 'U':
                        if (i == 0) result.Add(c);
                        break;
                    case 'B':
                        if (!(i == input.Length - 1 && i > 0 && input[i - 1] == 'M'))
                            result.Add('P');
                        break;
                    case 'C':
                        if (i + 1 < input.Length && "EIY".Contains(input[i + 1]))
                            result.Add('S');
                        else
                            result.Add('K');
                        break;
                    case 'D':
                        if (i + 2 < input.Length && input[i + 1] == 'G' && "EIY".Contains(input[i + 2]))
                            result.Add('J');
                        else
                            result.Add('T');
                        break;
                    case 'F': case 'J': case 'L': case 'M': case 'N': case 'R':
                        result.Add(c);
                        break;
                    case 'G':
                        if (i + 1 < input.Length && "EIY".Contains(input[i + 1]))
                            result.Add('J');
                        else if (!(i + 1 < input.Length && input[i + 1] == 'H'))
                            result.Add('K');
                        break;
                    case 'H':
                        if (i == 0 || !"AEIOU".Contains(input[i - 1]) || (i + 1 < input.Length && "AEIOU".Contains(input[i + 1])))
                            result.Add('H');
                        break;
                    case 'K':
                        if (i == 0 || input[i - 1] != 'C')
                            result.Add('K');
                        break;
                    case 'P':
                        if (i + 1 < input.Length && input[i + 1] == 'H')
                        {
                            result.Add('F');
                            i++;
                        }
                        else
                            result.Add('P');
                        break;
                    case 'Q':
                        result.Add('K');
                        break;
                    case 'S':
                        if (i + 1 < input.Length && input[i + 1] == 'H')
                        {
                            result.Add('X');
                            i++;
                        }
                        else
                            result.Add('S');
                        break;
                    case 'T':
                        if (i + 1 < input.Length && input[i + 1] == 'H')
                        {
                            result.Add('0'); // Theta sound
                            i++;
                        }
                        else if (!(i + 2 < input.Length && input.Substring(i, 3) == "TCH"))
                            result.Add('T');
                        break;
                    case 'V':
                        result.Add('F');
                        break;
                    case 'W': case 'Y':
                        if (i + 1 < input.Length && "AEIOU".Contains(input[i + 1]))
                            result.Add(c);
                        break;
                    case 'X':
                        result.Add('K');
                        result.Add('S');
                        break;
                    case 'Z':
                        result.Add('S');
                        break;
                }

                i++;
            }

            return new string(result.ToArray());
        }

        /// <summary>
        /// Find best fuzzy match from a list of candidates.
        /// Returns the best match and its similarity score.
        /// </summary>
        public (string? BestMatch, double Similarity) FindBestMatch(
            string source,
            IEnumerable<string> candidates,
            string algorithm = "Levenshtein",
            double minThreshold = 0.0)
        {
            string? bestMatch = null;
            double bestSimilarity = 0.0;

            foreach (var candidate in candidates)
            {
                double similarity = CalculateSimilarity(source, candidate, algorithm);
                if (similarity > bestSimilarity && similarity >= minThreshold)
                {
                    bestSimilarity = similarity;
                    bestMatch = candidate;
                }
            }

            return (bestMatch, bestSimilarity);
        }
    }

    /// <summary>
    /// Detailed result of composite scoring for UI display.
    /// </summary>
    public class CompositeMatchResult
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public double CompositeScore { get; set; }
        public bool IsExactMatch { get; set; }
        public bool IsNicknameMatch { get; set; }
        public string? MatchReason { get; set; }
        public Dictionary<string, AlgorithmScore> AlgorithmScores { get; set; } = new();
    }

    /// <summary>
    /// Individual algorithm score in a composite match.
    /// </summary>
    public class AlgorithmScore
    {
        public string Algorithm { get; set; } = "";
        public double Score { get; set; }
        public double Weight { get; set; }
        public double WeightedScore { get; set; }
    }
}
