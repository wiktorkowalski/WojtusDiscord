using System.Text.RegularExpressions;
using EventListenerService.Data;
using EventListenerService.Models;
using Microsoft.EntityFrameworkCore;
using NetCord.Services.ApplicationCommands;

namespace EventListenerService.Modules;

[SlashCommand("voice", "Voice command")]
public partial class VoiceModule : ApplicationCommandModule<ApplicationCommandContext>
{
  [SubSlashCommand("status", "Update channel status when you join a channel")]
  public partial class VoiceStatusModule(IServiceScopeFactory scopeFactory) : ApplicationCommandModule<ApplicationCommandContext>
  {
    private readonly Regex emojiRegex = EmojiRegex();

    [SubSlashCommand("set", "Status to set")]
    public async Task<string> SetStatusAsync(
      [SlashCommandParameter(Description = "Emoji")] string emoji)
    {

      if (!emojiRegex.IsMatch(emoji))
      {
        return $"{emoji} is not a valid emoji";
      }

      using var scope = scopeFactory.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<WojtusContext>();

      var userEmoji = await db.UserEmojis.FirstOrDefaultAsync(ue => ue.ID == Context.User.Id);

      if (userEmoji == null)
      {
        userEmoji = new UserEmoji { ID = Context.User.Id, Emoji = emoji };
        await db.UserEmojis.AddAsync(userEmoji);
      }
      else
      {
        userEmoji.Emoji = emoji;
        db.UserEmojis.Update(userEmoji);
      }

      await db.SaveChangesAsync();

      return $"{emoji} will be applied to channel status when you join voice!";
    }

    [SubSlashCommand("clear", "Clear status associated with you")]
    public async Task<string> ClearStatusAsync()
    {
      using var scope = scopeFactory.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<WojtusContext>();

      var userEmoji = await db.UserEmojis.Where(userEmoji => userEmoji.ID == Context.User.Id).FirstOrDefaultAsync();
      if (userEmoji == null)
      {
        return "No status is currently associated with you.";
      }
      db.Remove(userEmoji);
      await db.SaveChangesAsync();
      return $"{userEmoji.Emoji} will no longer be applied to channel status when you join voice!";
    }

    [GeneratedRegex(@"<:\S+:\d+>", RegexOptions.Compiled)]
    private static partial Regex EmojiRegex();
  }
}
