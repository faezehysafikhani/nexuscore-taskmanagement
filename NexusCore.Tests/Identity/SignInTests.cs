using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Security;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure.Migrations;
using NexusCore.Infrastructure.Security;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Tests.Identity;

/// <summary>Sign-in names (username, mobile number) and the sign-in CAPTCHA / attempt limits.</summary>
public sealed class SignInTests
{
    // ---------------------------------------------------------------- mobile numbers

    [Theory]
    [InlineData("09121234567", "09121234567")]
    [InlineData("0912 123 4567", "09121234567")]
    [InlineData("0912-123-4567", "09121234567")]
    [InlineData("(0912) 123.4567", "09121234567")]
    [InlineData("9121234567", "09121234567")]
    [InlineData("989121234567", "09121234567")]
    [InlineData("+989121234567", "09121234567")]
    [InlineData("+98 912 123 4567", "09121234567")]
    [InlineData("+98 0912 123 4567", "09121234567")]
    [InlineData("00989121234567", "09121234567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷", "09121234567")]
    [InlineData("٠٩١٢١٢٣٤٥٦٧", "09121234567")]
    [InlineData("+44 7911 123456", "+447911123456")]
    [InlineData("0044 7911 123456", "+447911123456")]
    public void PhoneNumber_IsBroughtIntoOneCanonicalForm(string typed, string canonical) =>
        Assert.Equal(canonical, PhoneNumber.Normalize(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0912123456")]      // one digit short
    [InlineData("091212345678")]    // one digit long
    [InlineData("02112345678")]     // Tehran landline, not a mobile
    [InlineData("+98 21 1234 5678")] // landline with country code
    [InlineData("0912abc4567")]
    [InlineData("user@example.com")]
    [InlineData("alice")]
    [InlineData("+0123456789")]
    [InlineData("12++34")]
    public void PhoneNumber_RejectsWhatIsNotAMobileNumber(string typed) =>
        Assert.Null(PhoneNumber.Normalize(typed));

    [Theory]
    [InlineData("alice", true)]
    [InlineData("Alice.Smith-2", true)]
    [InlineData("a_b", true)]
    [InlineData("ab", false)]           // too short
    [InlineData("9alice", false)]       // must start with a letter: never mistaken for a number
    [InlineData("alice@example.com", false)]
    [InlineData("علی", false)]
    [InlineData("", false)]
    public void Username_Rules(string value, bool valid) => Assert.Equal(valid, Username.IsValid(value));

    [Fact]
    public void User_StoresThePhoneNumberInCanonicalForm_AndRefusesAnInvalidNewOne()
    {
        var user = new User(Guid.NewGuid(), Guid.NewGuid(), null, "No Email", "hash");
        Assert.Null(user.Email);

        user.UpdateContactDetails("noemail", "+98 912 000 1111", notifySms: true);
        Assert.Equal("09120001111", user.PhoneNumber);
        Assert.Equal("noemail", user.Username);

        Assert.Throws<ArgumentException>(() => user.UpdateContactDetails("noemail", "not a number", true));
        Assert.Equal("09120001111", user.PhoneNumber);

        user.UpdateContactDetails("noemail", null, true);
        Assert.Null(user.PhoneNumber);
    }

    // ---------------------------------------------------------------- CAPTCHA and limits

    private sealed class Client : ICurrentUserContext
    {
        public Guid? UserId => null;
        public Guid? TenantId => null;
        public string? Email => null;
        public string? IpAddress { get; set; } = "10.0.0.1";
    }

    private static (LoginProtection Protection, Client Client, List<string> Codes) Create(
        IDistributedCache? cache = null, Action<LoginProtectionOptions>? configure = null, Client? client = null)
    {
        var options = new LoginProtectionOptions();
        configure?.Invoke(options);
        client ??= new Client();
        var codes = new List<string>();
        var protection = new LoginProtection(
            cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            client,
            Options.Create(options))
        {
            GenerateCode = length =>
            {
                var code = string.Concat(Enumerable.Range(0, length).Select(i => (char)('1' + (codes.Count + i) % 9)));
                codes.Add(code);
                return code;
            }
        };
        return (protection, client, codes);
    }

    [Fact]
    public async Task FirstAttempt_NeedsNoCaptcha_AFailedOneMakesTheNextNeedIt()
    {
        var (protection, _, _) = Create();

        Assert.True((await protection.BeforeLoginAttemptAsync("alice", null, null, default)).IsSuccess);
        Assert.True(await protection.RecordFailedLoginAsync("alice", default));

        var next = await protection.BeforeLoginAttemptAsync("alice", null, null, default);
        Assert.Equal("captcha.required", next.Error.Code);
    }

    [Fact]
    public async Task Captcha_RightAnswerPasses_OnlyOnce_WrongAnswerIsRefusedAndBurnsIt()
    {
        var (protection, _, codes) = Create();
        await protection.RecordFailedLoginAsync("alice", default);

        var wrong = (await protection.IssueCaptchaAsync(default)).Value!;
        Assert.Equal("captcha.invalid", (await protection.BeforeLoginAttemptAsync("alice", wrong.CaptchaId, "00000", default)).Error.Code);
        // The right answer no longer helps: the CAPTCHA was used up by the wrong one.
        Assert.Equal("captcha.invalid", (await protection.BeforeLoginAttemptAsync("alice", wrong.CaptchaId, codes[0], default)).Error.Code);

        var right = (await protection.IssueCaptchaAsync(default)).Value!;
        Assert.True((await protection.BeforeLoginAttemptAsync("alice", right.CaptchaId, codes[1], default)).IsSuccess);
        // Replaying a solved CAPTCHA fails.
        Assert.Equal("captcha.invalid", (await protection.BeforeLoginAttemptAsync("alice", right.CaptchaId, codes[1], default)).Error.Code);
    }

    [Fact]
    public async Task Captcha_AcceptsPersianDigits_AndIsNotInTheResponse()
    {
        var (protection, _, codes) = Create();
        await protection.RecordFailedLoginAsync("alice", default);
        var challenge = (await protection.IssueCaptchaAsync(default)).Value!;

        Assert.StartsWith("data:image/png;base64,", challenge.ImageDataUrl);
        Assert.DoesNotContain(codes[0], challenge.ImageDataUrl);
        Assert.DoesNotContain(codes[0], challenge.CaptchaId);

        var persian = string.Concat(codes[0].Select(c => (char)('۰' + (c - '0'))));
        Assert.True((await protection.BeforeLoginAttemptAsync("alice", challenge.CaptchaId, " " + persian + " ", default)).IsSuccess);
    }

    [Fact]
    public async Task Captcha_BelongsToTheClientThatRequestedIt()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var (owner, _, codes) = Create(cache);
        var (other, _, _) = Create(cache, client: new Client { IpAddress = "10.0.0.99" });

        var challenge = (await owner.IssueCaptchaAsync(default)).Value!;
        await other.RecordFailedLoginAsync("alice", default);

        Assert.Equal("captcha.invalid", (await other.BeforeLoginAttemptAsync("alice", challenge.CaptchaId, codes[0], default)).Error.Code);
    }

    [Fact]
    public async Task CaptchaState_IsOnTheServer_SoANewClientSessionStillNeedsIt_AndSuccessClearsIt()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var (first, _, _) = Create(cache);
        await first.RecordFailedLoginAsync("0912 000 2222", default);

        // A "page reload": a new service instance, same server state. Another spelling of the
        // same number shares the counter.
        var (second, _, codes) = Create(cache);
        Assert.Equal("captcha.required", (await second.BeforeLoginAttemptAsync("+989120002222", null, null, default)).Error.Code);

        var challenge = (await second.IssueCaptchaAsync(default)).Value!;
        Assert.True((await second.BeforeLoginAttemptAsync("09120002222", challenge.CaptchaId, codes[0], default)).IsSuccess);
        await second.RecordSuccessfulLoginAsync("09120002222", default);

        Assert.True((await second.BeforeLoginAttemptAsync("09120002222", null, null, default)).IsSuccess);
    }

    [Fact]
    public async Task FailuresFromOneClient_AgainstAnyName_MakeItsNextAttemptNeedACaptcha()
    {
        var (protection, _, _) = Create();
        await protection.RecordFailedLoginAsync("alice", default);

        Assert.Equal("captcha.required", (await protection.BeforeLoginAttemptAsync("bob", null, null, default)).Error.Code);
    }

    [Fact]
    public async Task TooManyFailuresForOneName_AreRefusedForAWhile()
    {
        var (protection, _, codes) = Create(configure: o => o.MaxFailedAttemptsPerIdentifier = 3);
        for (var i = 0; i < 3; i++)
        {
            await protection.RecordFailedLoginAsync("alice", default);
        }

        var challenge = (await protection.IssueCaptchaAsync(default)).Value!;
        var result = await protection.BeforeLoginAttemptAsync("alice", challenge.CaptchaId, codes[0], default);
        Assert.Equal("too_many_requests", result.Error.Code);
    }

    [Fact]
    public async Task PerClientLimits_ApplyToEveryAnonymousAction()
    {
        var (protection, _, _) = Create(configure: o => o.MaxRegistrationsPerClient = 2);

        Assert.True((await protection.ThrottleAsync(AuthAction.Register, default)).IsSuccess);
        Assert.True((await protection.ThrottleAsync(AuthAction.Register, default)).IsSuccess);
        Assert.Equal("too_many_requests", (await protection.ThrottleAsync(AuthAction.Register, default)).Error.Code);
        // Separate budgets per action.
        Assert.True((await protection.ThrottleAsync(AuthAction.ForgotPassword, default)).IsSuccess);
    }

    [Fact]
    public void CaptchaImage_IsAValidPng()
    {
        var png = CaptchaImage.Render("48213");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(200, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
        Assert.Equal(70, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
        // Random every time: two renders of the same code differ.
        Assert.NotEqual(png, CaptchaImage.Render("48213"));
    }

    // ---------------------------------------------------------------- migration SQL = domain rule

    /// <summary>
    /// The migration rewrites existing numbers in SQL; it must give exactly what the application
    /// gives for the same input, or existing users could not sign in with their number.
    /// Needs a local SQL Server; skipped (passes) without one.
    /// </summary>
    [Fact]
    public async Task MigrationPhoneSql_GivesTheSameResultAsTheDomainRule()
    {
        string[] inputs =
        [
            "09121234567", "0912 123 4567", "0912-123-4567", "(0912) 123.4567", "9121234567", "989121234567",
            "+989121234567", "+98 912 123 4567", "+98 0912 123 4567", "00989121234567", "۰۹۱۲۱۲۳۴۵۶۷",
            "٠٩١٢١٢٣٤٥٦٧", "+44 7911 123456", "0044 7911 123456", "0912123456", "02112345678",
            "+98 21 1234 5678", "0912abc4567", "+0123456789", "12345",
        ];

        await using var connection = new SqlConnection("Server=.;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3");
        try
        {
            await connection.OpenAsync();
        }
        catch (SqlException)
        {
            return; // no SQL Server here
        }

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE #Users ([Id] int NOT NULL, [PhoneNumber] nvarchar(32) NULL);";
            await create.ExecuteNonQueryAsync();
        }

        for (var i = 0; i < inputs.Length; i++)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO #Users VALUES (@id, @phone);";
            insert.Parameters.AddWithValue("@id", i);
            insert.Parameters.AddWithValue("@phone", inputs[i]);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var normalize = connection.CreateCommand())
        {
            normalize.CommandText = SignInByUsernameOrPhoneAndRemoveTelegram.NormalizePhoneNumbersSql.Replace("[identity].[Users]", "#Users");
            await normalize.ExecuteNonQueryAsync();
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT [Id], [PhoneNumber] FROM #Users ORDER BY [Id];";
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var input = inputs[reader.GetInt32(0)];
            // Unrecognisable values are left exactly as they were.
            var expected = PhoneNumber.Normalize(input) ?? input;
            Assert.Equal(expected, reader.GetString(1));
        }
    }
}
