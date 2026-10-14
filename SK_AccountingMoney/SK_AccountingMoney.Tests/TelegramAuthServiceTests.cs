using System.Security.Cryptography;
using System.Text;
using SK_AccountingMoney.Services;

namespace SK_AccountingMoney.Tests
{
    public class TelegramAuthServiceTests
    {
        private const string BotToken = "123456:TEST-token-for-unit-tests";
        private const string UserJson = "{\"id\":111222333,\"first_name\":\"Test\",\"username\":\"test_user\"}";

        private readonly TelegramAuthService _service = new(BotToken);

        /// <summary>
        /// Builds initData the way Telegram does: values URL-encoded, hash = HMAC-SHA256 of the
        /// sorted "key=value" lines (hash excluded, signature included) with key HMAC("WebAppData", token)
        /// </summary>
        private static string BuildInitData(string botToken, long authDate, string userJson = UserJson)
        {
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth_date"] = authDate.ToString(),
                ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
                ["signature"] = "dGVzdC1zaWduYXR1cmU",
                ["user"] = userJson
            };

            var dataCheckString = string.Join("\n", fields.Select(f => $"{f.Key}={f.Value}"));
            using var keyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
            var secretKey = keyHmac.ComputeHash(Encoding.UTF8.GetBytes(botToken));
            using var hmac = new HMACSHA256(secretKey);
            var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

            // Reverse order on purpose: the service must sort the fields itself
            return string.Join("&", fields.Reverse().Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"))
                + $"&hash={hash}";
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [Fact]
        public void ValidateFull_AcceptsCorrectlySignedInitData()
        {
            var result = _service.ValidateFull(BuildInitData(BotToken, Now()));

            Assert.True(result.IsValid, result.Error);
            Assert.Equal(111222333, result.UserData!.Id);
            Assert.Equal("test_user", result.UserData.UserName);
        }

        [Fact]
        public void ValidateFull_RejectsInitDataSignedWithAnotherToken()
        {
            var result = _service.ValidateFull(BuildInitData("999999:another-bot-token", Now()));

            Assert.False(result.IsValid);
            Assert.Equal("Invalid signature", result.Error);
        }

        [Fact]
        public void ValidateFull_RejectsTamperedUserId()
        {
            var originalUser = "user=" + Uri.EscapeDataString(UserJson);
            var forgedUser = "user=" + Uri.EscapeDataString(UserJson.Replace("111222333", "100000001"));
            var initData = BuildInitData(BotToken, Now()).Replace(originalUser, forgedUser);
            Assert.Contains(forgedUser, initData);

            var result = _service.ValidateFull(initData);

            Assert.False(result.IsValid);
            Assert.Equal("Invalid signature", result.Error);
        }

        [Fact]
        public void ValidateFull_RejectsInitDataWithoutHash()
        {
            var initData = BuildInitData(BotToken, Now());
            initData = initData[..initData.IndexOf("&hash=", StringComparison.Ordinal)];

            Assert.False(_service.ValidateFull(initData).IsValid);
        }

        [Theory]
        [InlineData("not-hex")]
        [InlineData("abcd")]
        public void ValidateInitData_RejectsMalformedHash(string hash)
        {
            var initData = BuildInitData(BotToken, Now());
            initData = initData[..initData.IndexOf("&hash=", StringComparison.Ordinal)] + "&hash=" + hash;

            Assert.False(_service.ValidateInitData(initData));
        }

        [Fact]
        public void ValidateFull_RejectsExpiredAuthDate()
        {
            var twoDaysAgo = Now() - 2 * 86400;

            var result = _service.ValidateFull(BuildInitData(BotToken, twoDaysAgo));

            Assert.False(result.IsValid);
            Assert.Equal("Auth data expired", result.Error);
        }
    }
}
