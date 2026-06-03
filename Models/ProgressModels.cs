namespace GalExcleTools;

internal sealed record ProgressUpdate(string Message, double Percent, string? Detail = null, bool IsIndeterminate = false);
