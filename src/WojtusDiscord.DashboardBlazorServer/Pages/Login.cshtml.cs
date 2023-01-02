using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WojtusDiscord.DashboardBlazorServer.Pages
{
    public class LoginModel : PageModel
    {
        public async Task OnGet(string redirectUri)
        {
            var authenticationProperties = new AuthenticationProperties()
            {
                RedirectUri = redirectUri
            };

            await HttpContext.ChallengeAsync("GitHub", authenticationProperties); // proper schema name
        }
    }
}
