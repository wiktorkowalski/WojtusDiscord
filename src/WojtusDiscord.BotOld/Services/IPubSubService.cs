using System.Threading.Tasks;

namespace WojtusDiscord.Bot.Services
{
    public interface IPubSubService
    {
        public Task RegisterBot();
    }
}