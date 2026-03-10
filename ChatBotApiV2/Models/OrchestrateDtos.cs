using ClbModChatbot;

namespace ChatBotApiV2.Models
{
    public sealed class OrchestrateRequestDto
    {
        public string? text { get; set; }
        public List<ClsModChatMessageItem>? messages { get; set; }
        public List<string>? images { get; set; }
        public string? init_image_path { get; set; }
        public double? strength { get; set; }
        public string? output_path { get; set; }
        public string? model { get; set; }
        public object? providerPreference { get; set; } // string or array
        public string? tool_hint { get; set; }
        public string? idChat { get; set; }
        public string? session_id { get; set; }
        public bool isNewChat { get; set; }
    }

    public sealed class OrchestrateResponseDto
    {
        public string? action { get; set; }
        public string? content { get; set; }
        public string? image_path { get; set; }
        public List<SearchResultDto>? sources { get; set; }
        public RouterDebugDto? debug { get; set; }
    }

    public sealed class SearchResultDto
    {
        public string? title { get; set; }
        public string? url { get; set; }
        public string? snippet { get; set; }
    }

    public sealed class RouterDebugDto
    {
        public string? action { get; set; }
        public string? reason { get; set; }
        public double? confidence { get; set; }
    }
}
