using System.ComponentModel.DataAnnotations;

namespace ClbModChatbot
{
    public class ClsModUser
    {
        [Key]
        public string IdUser { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public DateTime RegisterDate { get; set; }  
        public string? Password { get; set; } 
        public string? Role { get; set; }

    }
}
