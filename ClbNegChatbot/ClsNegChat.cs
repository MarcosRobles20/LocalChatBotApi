using ClbDatChatbot;
using ClbModChatbot;
using Microsoft.Extensions.Configuration;

namespace ClbNegChatbot
{
    public class ClsNegChat
    {
        private readonly ClsDatChat _datChat;

        public ClsNegChat(IConfiguration configuration)
        {
            _datChat = new ClsDatChat(configuration);
        }

        public List<ClsModChat> GetChatsWithIdUser(ClsModChatRequest request)
        {
            return _datChat.GetChatsWithIdUser(request);
        }

        public ClsModChat GetChatsWithIdChat(ClsModChatRequest request)
        {
            return _datChat.GetChatWithIdChat(request);
        }
    }
}
