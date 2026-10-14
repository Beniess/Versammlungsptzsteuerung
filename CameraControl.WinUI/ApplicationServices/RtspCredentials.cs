namespace CameraControl_WinUI;

public sealed record RtspCredential(string UserName, string Password)
{
    public override string ToString() => "RtspCredential { Password = <hidden> }";
}

public interface IRtspCredentialStore
{
    Task SaveAsync(string target, string userName, string password, CancellationToken cancellationToken = default);
    Task<RtspCredential?> ReadAsync(string target, CancellationToken cancellationToken = default);
    Task DeleteAsync(string target, CancellationToken cancellationToken = default);
}
