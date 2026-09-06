using System.Globalization;
using api.Devices;

namespace Agrumy.Api.Tests;

/// Reads the SAME threshold_vectors.csv AgrumyFirmware's test_native_threshold_logic reads, so the two independently-implemented formulas can't silently drift apart.
public class ThresholdVectorTests
{
    public static IEnumerable<object[]> Vectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestVectors", "threshold_vectors.csv");
        bool headerSkipped = false;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            if (!headerSkipped)
            {
                headerSkipped = true;
                continue;
            }
            string[] f = line.Split(',');
            yield return new object[]
            {
                f[0], // name
                bool.Parse(f[1]), // currentlyOn
                double.Parse(f[2], CultureInfo.InvariantCulture), // reading
                double.Parse(f[3], CultureInfo.InvariantCulture), // threshold
                double.Parse(f[4], CultureInfo.InvariantCulture), // hysteresis
                bool.Parse(f[5]), // turnsOnAboveThreshold
                bool.Parse(f[6]), // expected
            };
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void ComputeThresholdState_MatchesSharedVector(string name, bool currentlyOn, double reading, double threshold, double hysteresis, bool turnsOnAboveThreshold, bool expected)
    {
        bool actual = RuleConditionEvaluator.ComputeThresholdState(currentlyOn, reading, threshold, hysteresis, turnsOnAboveThreshold);
        Assert.True(actual == expected, $"{name}: expected {expected}, got {actual}");
    }
}
