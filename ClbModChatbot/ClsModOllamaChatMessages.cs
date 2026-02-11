namespace ClbModChatbot
{
    /// <summary>
    /// Modelo para enviar mensajes al endpoint /api/chat de Ollama
    /// </summary>
    public class ClsModOllamaChatMessages
    {
        public string IdUser { get; set; } = string.Empty;
        public string? Model { get; set; }
        public string? IdChat { get; set; }
        public List<ClsModChatMessageItem> Messages { get; set; } = new List<ClsModChatMessageItem>();
        public bool Stream { get; set; } = false;
    }

    /// <summary>
    /// Representa un mensaje individual en la conversación para el API de chat
    /// </summary>
    public class ClsModChatMessageItem
    {
        public string Role { get; set; } = string.Empty; // "system", "user", "assistant"
        public string Content { get; set; } = string.Empty;
    }
}