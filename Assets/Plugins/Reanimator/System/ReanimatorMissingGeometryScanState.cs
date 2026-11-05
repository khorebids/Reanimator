using System;

internal sealed class ReanimatorMissingGeometryScanState
{
    private string attemptedPath = string.Empty;
    private string scheduledPath = string.Empty;
    private string alertPath = string.Empty;

    public bool HasAlertForPath(string normalizedPath)
    {
        return !string.IsNullOrEmpty(normalizedPath) &&
               string.Equals(alertPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldScheduleScan(string normalizedPath)
    {
        return !string.IsNullOrEmpty(normalizedPath) &&
               !string.Equals(attemptedPath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(scheduledPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    public void MarkScheduled(string normalizedPath)
    {
        scheduledPath = normalizedPath ?? string.Empty;
    }

    public bool IsScheduledPath(string normalizedPath)
    {
        return !string.IsNullOrEmpty(normalizedPath) &&
               string.Equals(scheduledPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    public void ClearScheduledPath(string normalizedPath)
    {
        if (IsScheduledPath(normalizedPath))
            scheduledPath = string.Empty;
    }

    public void RecordScanResult(string normalizedPath, bool hasMissingGeometry)
    {
        if (string.IsNullOrEmpty(normalizedPath))
            return;

        attemptedPath = normalizedPath;
        if (hasMissingGeometry)
        {
            alertPath = normalizedPath;
        }
        else if (HasAlertForPath(normalizedPath))
        {
            alertPath = string.Empty;
        }
    }

    public void ClearAlert(string normalizedPath)
    {
        if (HasAlertForPath(normalizedPath))
            alertPath = string.Empty;
    }

    public void Reset()
    {
        attemptedPath = string.Empty;
        scheduledPath = string.Empty;
        alertPath = string.Empty;
    }
}
