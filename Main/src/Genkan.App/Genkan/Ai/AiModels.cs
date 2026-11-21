namespace Genkan.Ai;

public sealed record AiChatRequest(
    string? ProviderId,
    string? ChatId,
    string? Title,
    List<ChatMessage>? Messages);

public sealed record AiKeyRequest(string? ProviderId, string? Key);

public sealed record AiProviderView(
    string Id,
    string Name,
    string Kind,
    string Model,
    bool HasKey,
    bool Enabled);
