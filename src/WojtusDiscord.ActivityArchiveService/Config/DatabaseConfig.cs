using Microsoft.AspNetCore.Hosting.Server;

namespace WojtusDiscord.ActivityArchiveService.Config
{
    public class DatabaseConfig
    {
        public static readonly string Prefix = "DB";

        public string Host { get; set; }
        public string Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Name { get; set; }

        public string ToConnectionString()
        {
            return $"Server={Host};Port={Port};User ID={User};Database={Name};Password={Password};";
        }
    }
}
