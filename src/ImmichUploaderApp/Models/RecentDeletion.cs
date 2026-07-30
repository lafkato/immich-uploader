namespace ImmichUploaderApp.Models;

/// Reason is a short, already-localized phrase (e.g. "Poistettu Immichistä" / "Poistettu
/// paikallisesti -> roskakoriin Immichissä") describing which side triggered the deletion.
public sealed record RecentDeletion(string FileName, DateTime DeletedAtLocal, string Reason);
