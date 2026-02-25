namespace ClbModChatbot
{
    /// <summary>
    /// Representa un chunk individual emitido por el endpoint SSE <c>/api/chat/chatStream</c>
    /// durante la generación de respuesta en tiempo real.
    /// <para>
    /// El stream sigue este ciclo de vida ordenado:
    /// <list type="number">
    ///   <item><c>thinking_start</c> — El modelo inicia el razonamiento (CoT)</item>
    ///   <item><c>thinking</c>       — Chunks del razonamiento interno (múltiples)</item>
    ///   <item><c>thinking_end</c>   — El modelo terminó de razonar</item>
    ///   <item><c>response</c>       — Chunks de la respuesta final (múltiples)</item>
    ///   <item><c>done</c>           — Stream completo, incluye metadata del chat</item>
    /// </list>
    /// En caso de error en cualquier punto se emite un chunk de tipo <c>error</c>.
    /// </para>
    /// <para>
    /// Modelos que soportan CoT (<c>thinking</c>) vía Ollama:
    /// Qwen3, DeepSeek-R1, DeepSeek-v3.1, GPT-OSS.
    /// Modelos sin CoT solo emiten <c>response</c> y <c>done</c>.
    /// </para>
    /// </summary>
    public class ClsModStreamChunk
    {
        /// <summary>
        /// Tipo del chunk. Determina cómo debe procesarlo el frontend.
        /// <list type="table">
        ///   <listheader><term>Valor</term><description>Significado</description></listheader>
        ///   <item><term>thinking_start</term><description>Inicio del Chain-of-Thought. <c>Content</c> es null. El frontend debe mostrar el bloque "Pensando..."</description></item>
        ///   <item><term>thinking</term><description>Chunk del razonamiento interno. <c>Content</c> contiene texto del CoT a acumular.</description></item>
        ///   <item><term>thinking_end</term><description>Fin del razonamiento. <c>Content</c> es null. El frontend debe colapsar el bloque CoT.</description></item>
        ///   <item><term>response</term><description>Chunk de la respuesta final. <c>Content</c> contiene texto a mostrar en stream.</description></item>
        ///   <item><term>done</term><description>Stream completado. <c>IdChat</c>, <c>IsNewChat</c> y <c>Model</c> están poblados. <c>Content</c> es null.</description></item>
        ///   <item><term>error</term><description>Error ocurrido. <c>Error</c> contiene el mensaje. El stream se cierra después de este chunk.</description></item>
        /// </list>
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Texto del chunk. Solo presente en tipos <c>thinking</c> y <c>response</c>.
        /// Debe acumularse en el frontend para construir el texto completo.
        /// Es <c>null</c> en <c>thinking_start</c>, <c>thinking_end</c>, <c>done</c> y <c>error</c>.
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Identificador del chat. Solo presente en el chunk de tipo <c>done</c>.
        /// Si el chat fue creado automáticamente, este es el ID generado durante el stream.
        /// Debe guardarse en el frontend para futuras peticiones al mismo chat.
        /// </summary>
        public string? IdChat { get; set; }

        /// <summary>
        /// Indica si el chat fue creado durante este stream. Solo presente en tipo <c>done</c>.
        /// <c>true</c> = chat nuevo creado automáticamente.
        /// <c>false</c> = mensaje agregado a un chat existente.
        /// Útil para que el frontend actualice la lista lateral de chats.
        /// </summary>
        public bool? IsNewChat { get; set; }

        /// <summary>
        /// Nombre del modelo utilizado (ej: <c>qwen3:8b</c>). Solo presente en tipo <c>done</c>.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Mensaje de error. Solo presente en tipo <c>error</c>.
        /// El stream se cierra inmediatamente después de emitir este chunk.
        /// </summary>
        public string? Error { get; set; }
    }
}
