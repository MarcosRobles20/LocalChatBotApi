namespace ClbModChatbot
{
    /// <summary>
    /// Request para obtener mensajes de un chat con opciones de paginación
    /// </summary>
    public class ClsModChatMessagesRequest
    {
        public string IdUser { get; set; } = string.Empty;
        public string IdChat { get; set; } = string.Empty;
        public int? MaxMessages { get; set; } = 50;
        public int? PageNumber { get; set; } = 1;
        public bool IncludeDeleted { get; set; } = false;
    }
}