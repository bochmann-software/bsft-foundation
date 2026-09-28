namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>The second outbox's side effect: what a committed intent actually does, later.</summary>
public sealed class RecordingProvisioning : IOutboxSideEffect<string>
{
    /// <summary>The tenants provisioned, in order.</summary>
    public List<string> Provisioned { get; } = [];

    /// <summary>The tenants whose provisioning fails, with the failure it fails by.</summary>
    public Dictionary<string, Exception> Failures { get; } = [];

    /// <inheritdoc />
    public Task ApplyAsync(string payload, CancellationToken ct = default)
    {
        if (Failures.TryGetValue(payload, out var failure))
            return Task.FromException(failure);

        Provisioned.Add(payload);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string Describe(string payload) => $"provisioning of {payload}";
}
