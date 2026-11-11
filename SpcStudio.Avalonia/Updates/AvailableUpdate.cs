namespace SpcStudio;

// A release newer than the running app: its version and where its download for this system is.
public sealed record AvailableUpdate(Version Version, Uri DownloadUri);
