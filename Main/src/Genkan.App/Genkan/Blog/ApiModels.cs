namespace Genkan.Blog;

public sealed record ReactRequest(string Kind);

public sealed record AnnotateRequest(int BlockIndex, string BlockHash, string Author, string Body);

public sealed record PreviewRequest(string Markdown);
