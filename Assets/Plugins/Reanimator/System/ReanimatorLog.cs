#if UNITY_EDITOR
using UnityEngine;

internal static class ReanimatorLog
{
    public static void Trace(string message, bool enabled)
    {
        if (!enabled)
            return;

        Debug.Log(message);
    }

    public static void Error(string message)
    {
        Debug.LogError(message);
    }

    public static void Exception(System.Exception exception)
    {
        Debug.LogException(exception);
    }
}
#endif
