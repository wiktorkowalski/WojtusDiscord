using WojtusDiscord.API.Models.Auth;

namespace WojtusDiscord.API.Services.Auth
{
    public interface ITokenService
    {
        string BuildToken(LoginModel user);
    }
}