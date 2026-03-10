using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClbModChatbot
{
    public class ClsModStreamChunk
    {
        // Tipo de chunk: "thinking_start", "thinking", "thinking_end", "response", "image", "done", "error", "tool_call", "agent_event", etc.
        public string Type { get; set; } = string.Empty;

        // Contenido textual o JSON serializado según el tipo de evento
        public string? Content { get; set; }

        // Modelo de IA usado (opcional)
        public string? Model { get; set; }

        // Nombre de la herramienta si es un tool_call (opcional)
        public string? Tool { get; set; }

        // =============================
        // NUEVOS CAMPOS PARA agent_event
        // =============================

        // Tipo específico de evento de agente (por ejemplo "search_started", "summary_generated", etc.)
        [JsonPropertyName("event_type")]
        public string? event_type { get; set; }

        // Metadata adicional del evento (puede ser cualquier objeto serializable)
        public object? Metadata { get; set; }

        // =============================
        // CAMPOS EXISTENTES PARA CHAT
        // =============================

        // Id del chat al que pertenece el evento
        public string? IdChat { get; set; }

        // Indica si es un chat nuevo
        public bool IsNewChat { get; set; }

        // Para futuras extensiones, se puede usar para cualquier dato adicional
        public object? Extra { get; set; }

        // Constructor vacío por defecto
        public ClsModStreamChunk() { }

        // Constructor rápido para contenido simple
        public ClsModStreamChunk(string type, string? content = null)
        {
            Type = type;
            Content = content;
        }

        public string Error { get; set; }
    }
}   