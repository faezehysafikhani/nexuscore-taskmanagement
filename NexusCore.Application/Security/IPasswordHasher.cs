namespace NexusCore.Application.Security;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool Verify(string password, string passwordHash);
    string HashToken(string token);
}
