namespace ImmichUploaderApp.Models;

public sealed record RecentFailure(string FileName, string Message, DateTime OccurredAtLocal, bool WillRetry);
