namespace ClbModChatbot
{
    /// <summary>
    /// Respuesta del endpoint /api/chat de Ollama
    /// </summary>
    public class ClsModOllamaChatResponse
    {
        public string? model { get; set; }
        public string? created_at { get; set; }
        public ClsModChatMessageItem? message { get; set; }
        public bool done { get; set; }
    }
}