namespace Bsft.Messaging.RabbitMQ.Tests.Support;

/// <summary>
/// The valid starting point every test varies from.
/// </summary>
/// <remarks>
/// The credentials are deliberately implausible. No test in this suite connects to anything, so a
/// value that looks like a real account would only ever be a thing to copy by mistake.
/// </remarks>
internal static class TestOptions
{
    public const string UserName = "test-user";

    public const string Password = "not-a-real-password";

    public static BsftRabbitMQOptions Valid() => new()
    {
        ApplicationName = "orders",
        UserName = UserName,
        Password = Password,
    };
}
