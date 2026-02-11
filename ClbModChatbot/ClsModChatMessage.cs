using System;

namespace ClbModChatbot
{
    public class ClsModChatMessage
    {
        public long Id { get; set; }  
        public string IdChat { get; set; } = string.Empty;
        public string IdUser { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;  // 'user', 'assistant', 'system', 'tool'
        public string Content { get; set; } = string.Empty;  
        public string? Model { get; set; }  
        public int? TokenCount { get; set; } 
        public DateTime CreateDate { get; set; }  
        public int MessageOrder { get; set; }
        public bool IsDeleted { get; set; } = false;
        public string UserMessage => Role == "user" ? Content : string.Empty;
        public string AiResponse => Role == "assistant" ? Content : string.Empty;
        
    }
}