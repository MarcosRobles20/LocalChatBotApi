using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClbModChatbot
{
    public class ClsModOllamaChatRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string? Model { get; set; }
        public string? ChatId { get; set; }
    }
}
