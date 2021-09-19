using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System;
using Microsoft.Extensions.Logging;
using WojtusDiscord.API.Models.Auth;

namespace WojtusDiscord.API.Services.Auth
{
    public class TokenService : ITokenService
    {
        private readonly ILogger<TokenService> _logger;
        private readonly TimeSpan ExpiryDuration = new TimeSpan(0, 30, 0);
        private readonly string _key;
        private readonly string _issuer;

        public TokenService(ILogger<TokenService> logger)
        {
            _logger = logger;
            _key = Environment.GetEnvironmentVariable("JWTKey") ?? throw new ArgumentNullException("JWTKey");
            _issuer = Environment.GetEnvironmentVariable("JWTIssuer") ?? throw new ArgumentNullException("JWTIssuer");

            logger.LogDebug("TokenService Constructor");
        }

        public string BuildToken(LoginModel user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier,
                Guid.NewGuid().ToString())
             };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);
            var tokenDescriptor = new JwtSecurityToken(_issuer, _issuer, claims,
                expires: DateTime.Now.Add(ExpiryDuration), signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        }
    }
}
