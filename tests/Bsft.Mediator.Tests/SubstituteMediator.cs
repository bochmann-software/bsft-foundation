using System.Reflection;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A dispatcher a host supplies in place of the shipped one.</summary>
public sealed class SubstituteMediator : IMediator
{
    /// <inheritdoc />
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task SendAsync(ICommand command, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification => throw new NotSupportedException();
}
