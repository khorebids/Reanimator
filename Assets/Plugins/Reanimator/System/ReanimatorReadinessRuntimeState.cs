#if UNITY_EDITOR
using System;
using System.Collections.Generic;

internal static class ReanimatorReadinessRuntimeState
{
    private static readonly HashSet<string> passedCaseNames = new HashSet<string>(StringComparer.Ordinal);

    public static bool HasSelfTestResults { get; private set; }

    public static void BeginSelfTestRun()
    {
        HasSelfTestResults = true;
        passedCaseNames.Clear();
    }

    public static void RecordSelfTestResult(string caseName, bool passed)
    {
        if (string.IsNullOrWhiteSpace(caseName))
            return;

        HasSelfTestResults = true;
        if (passed)
            passedCaseNames.Add(caseName);
        else
            passedCaseNames.Remove(caseName);
    }

    public static bool HasPassed(string caseName)
    {
        return HasSelfTestResults &&
               !string.IsNullOrWhiteSpace(caseName) &&
               passedCaseNames.Contains(caseName);
    }
}
#endif
