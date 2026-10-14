using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SK_AccountingMoney.Data;
using SK_AccountingMoney.Models;

namespace SK_AccountingMoney.Tests
{
    public class AppDbContextTests : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public AppDbContextTests()
        {
            _connection.Open();
            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private AppDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        [Fact]
        public void TimestampsReadFromDatabase_AreUtc_AndSerializeWithZ()
        {
            var createdAt = new DateTime(2026, 10, 12, 12, 30, 0, DateTimeKind.Utc);

            using (var context = CreateContext())
            {
                var user = new User { TelegramId = 100000001, UserName = "anna_k" };
                context.Users.Add(user);
                context.Transactions.Add(new Transaction
                {
                    User = user,
                    Amount = 10m,
                    Description = "Groceries",
                    Type = "withdraw",
                    CreatedAt = createdAt
                });
                context.SaveChanges();
            }

            using (var context = CreateContext())
            {
                var stored = context.Transactions.Single();

                Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
                Assert.Equal(createdAt, stored.CreatedAt);
                Assert.Equal("\"2026-10-12T12:30:00Z\"", JsonSerializer.Serialize(stored.CreatedAt));
            }
        }

        [Fact]
        public void MonthFilter_StillMatchesStoredTimestamps()
        {
            using (var context = CreateContext())
            {
                var user = new User { TelegramId = 100000001, UserName = "anna_k" };
                context.Transactions.Add(new Transaction
                {
                    User = user,
                    Amount = 10m,
                    Description = "Fuel",
                    Type = "withdraw",
                    CreatedAt = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc)
                });
                context.SaveChanges();
            }

            using (var context = CreateContext())
            {
                var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddMonths(1).AddSeconds(-1);

                Assert.Equal(1, context.Transactions.Count(t => t.CreatedAt >= start && t.CreatedAt <= end));
            }
        }
    }
}
