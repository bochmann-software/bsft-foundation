using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>The bus a forwarder needs, deliberately never registered by the tests that use it.</summary>
public interface IUnregisteredBus
{
    /// <summary>Carries the notification to the outside world.</summary>
    /// <param name="notification">The notification to carry.</param>
    /// <returns>A task that completes once the notification has been carried.</returns>
    Task SendAsync(INotification notification);
}
