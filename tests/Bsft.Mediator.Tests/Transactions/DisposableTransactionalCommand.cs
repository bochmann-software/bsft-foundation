using Bsft.Core;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

// A command whose handler has [Transactional] AND a non-generic interface (IDisposable).
// This exercises TransactionBehavior.RequiresTransaction line 161:
// when iterating interfaces and an interface is not generic -> continue.
public record DisposableTransactionalCommand(string Value) : ICommand<string>;
