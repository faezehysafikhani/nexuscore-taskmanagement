using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Validators;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexusCore.Tests.Identity;

public sealed class UserPersonalDetailsTests
{
    [Theory]
    [InlineData(307199, true)]
    [InlineData(307200, true)]
    [InlineData(307201, false)]
    public void PersonnelPhotoLimit_CountsDecodedBytes_ForCreateAndUpdate(int bytes, bool accepted)
    {
        var image = "data:image/png;base64," + Convert.ToBase64String(new byte[bytes]);
        var create = new CreateUserRequest(Guid.NewGuid(), "1234567891", "علی", "محمدی", "09121234567", "Strong-pass-123", AvatarUrl: image);
        var update = new UpdateUserRequest("علی محمدی", true, AvatarUrl: image);
        Assert.Equal(accepted, new CreateUserRequestValidator().Validate(create).IsValid);
        Assert.Equal(accepted, new UpdateUserRequestValidator().Validate(update).IsValid);
    }

    [Fact]
    public async Task PersonalDetailsAndPhoto_RoundTripThroughPersistence_AndCanBeCleared()
    {
        var options = new DbContextOptionsBuilder<NexusCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var user = new User(Guid.NewGuid(), Guid.NewGuid(), null, "کاربر", "hash");
        user.SetPersonalDetails("  تهران  ", "  علی  ", new DateOnly(1990, 3, 21));
        user.SetAvatar("data:image/png;base64,aGVsbG8=");
        await using (var db = new NexusCoreDbContext(options)) { db.Users.Add(user); await db.SaveChangesAsync(); }
        await using (var db = new NexusCoreDbContext(options))
        {
            var saved = await db.Users.SingleAsync();
            Assert.Equal("تهران", saved.Address); Assert.Equal("علی", saved.FatherName);
            Assert.Equal(new DateOnly(1990, 3, 21), saved.BirthDate); Assert.Equal(user.AvatarUrl, saved.AvatarUrl);
            saved.SetPersonalDetails("", "", null); saved.SetAvatar(""); await db.SaveChangesAsync();
        }
        await using var verify = new NexusCoreDbContext(options);
        var cleared = await verify.Users.SingleAsync();
        Assert.Null(cleared.Address); Assert.Null(cleared.FatherName); Assert.Null(cleared.BirthDate); Assert.Null(cleared.AvatarUrl);
    }

    [Fact]
    public void AdminInput_RejectsFutureBirthDateAndUnsafePhoto()
    {
        var request = new CreateUserRequest(Guid.NewGuid(), "1234567891", "علی", "محمدی", "09121234567", "Strong-pass-123",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), AvatarUrl: "data:image/svg+xml;base64,AAAA");
        var result = new CreateUserRequestValidator().Validate(request);
        Assert.Contains(result.Errors, e => e.PropertyName == "BirthDate");
        Assert.Contains(result.Errors, e => e.PropertyName == "AvatarUrl");
    }
}
