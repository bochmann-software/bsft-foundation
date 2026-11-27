using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Bsft.Data.Tests.Fakes;

/// <summary>
/// A connection whose transactions commit and roll back fine but fail when they are disposed, the
/// way a broken connection fails a cleanup after the work is done.
/// </summary>
public sealed class FailingDisposalConnection(Exception disposalFailure) : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => "fake";

    public override string DataSource => "fake";

    public override string ServerVersion => "1";

    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open() => _state = ConnectionState.Open;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new FailingDisposalTransaction(this, isolationLevel, disposalFailure);

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

    private sealed class FailingDisposalTransaction(
        DbConnection connection, IsolationLevel isolationLevel, Exception disposalFailure) : DbTransaction
    {
        public override IsolationLevel IsolationLevel { get; } = isolationLevel;

        protected override DbConnection DbConnection { get; } = connection;

        public override void Commit()
        {
            // Committed; nothing to write in a fake.
        }

        public override void Rollback()
        {
            // Rolled back; nothing to undo in a fake.
        }

        public override ValueTask DisposeAsync() => throw disposalFailure;
    }
}
