namespace ClbModChatbot
{
    public class ClsModLoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public ClsModUser? User { get; set; }
    }
}