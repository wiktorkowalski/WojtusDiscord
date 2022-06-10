namespace WojtusDiscord.ArchiveService
{
    public class DatabaseProvider
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public DatabaseProvider(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public void InsertActivity(Activity activity)
        {
            using (var context = _serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<DatabaseContext>())
            {
                context.Activities.Add(activity);
                context.SaveChanges();
            }
        }

        public IEnumerable<Activity> GetAllActivities()
        {
            using (var context = _serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<DatabaseContext>())
            {
                return context.Activities.ToArray();
            }
        }
    }
}
