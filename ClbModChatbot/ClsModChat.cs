using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClbModChatbot
{
    public class ClsModChat
    {
        [Key]
        public string? IdChat { get; set; }
        public string? IdUser { get; set; }
        public string? Message { get; set; }
        public DateTime? LastModified { get; set; }
        public string? Title { get; set; }
    }
}
