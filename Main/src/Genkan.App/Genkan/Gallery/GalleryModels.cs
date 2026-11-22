namespace Genkan.Gallery;

public sealed record GalleryFavoriteRequest(
    string? Id,
    string? ImageKey,
    string? PreviewUrl,
    string? OriginalUrl,
    string? Source,
    string? Tags);

public sealed record GalleryTagRequest(string? Tag);

public sealed record GalleryHistoryRequest(string? Query, string? Source);
