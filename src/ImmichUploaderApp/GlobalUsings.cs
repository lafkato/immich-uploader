// Adding UseWPF alongside UseWindowsForms (for ThumbnailImaging's HEIC/RAW decode fallback via
// WIC) changes which namespaces the SDK's own implicit-usings generation includes - System.IO and
// System.Net.Http silently dropped out, breaking File/Directory/Path/FileInfo/HttpClient etc.
// across most of the codebase. Restoring them here in one place instead of touching every file.
global using System.IO;
global using System.Net.Http;
