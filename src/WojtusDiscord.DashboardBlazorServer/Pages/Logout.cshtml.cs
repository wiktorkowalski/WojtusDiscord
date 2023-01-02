using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WojtusDiscord.DashboardBlazorServer.Pages
{
    [Authorize]
    public class LogoutModel : PageModel
    {
        public async Task OnGet()
        {
            var authenticationProperties = new AuthenticationProperties()
            {
                RedirectUri = "/"
            };
            
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme, authenticationProperties);
        }
    }
}
