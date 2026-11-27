using System.Text.Json;


namespace Dsw2025Tpi.Data.Helper
{
    public static class DbCocntextExtensions
    {
        public static void Seedwork<T>(this  Dsw2025TpiContext context, string dataSource) where T : class
        {
            if (context.Set<T>().Any()) return;
            var path = Path.Combine(AppContext.BaseDirectory, dataSource);
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var entities = JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (entities == null || entities.Count == 0) return;
            context.Set<T>().AddRange(entities);
            context.SaveChanges();
        }
    }
}
