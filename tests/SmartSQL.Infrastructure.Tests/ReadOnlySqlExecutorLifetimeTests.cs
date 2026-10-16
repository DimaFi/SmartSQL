using SmartSQL.Application;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class ReadOnlySqlExecutorLifetimeTests
{
    private static readonly TimeSpan CompletionBound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CallbackAndReleaseFailuresStillDrainAndReleaseExactlyOnce()
    {
        var executor = new FaultingExecutor();
        var execution = executor.ExecuteAsync(new("SELECT 1"));
        await executor.Started.Task.WaitAsync(CompletionBound);

        var firstDisposal = executor.DisposeAsync().AsTask();
        await executor.CancellationObserved.Task.WaitAsync(CompletionBound);
        Assert.False(firstDisposal.IsCompleted);
        Assert.Equal(0, executor.ReleaseCount);

        executor.AllowCompletion.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(CompletionBound));
        var firstFailure = await Record.ExceptionAsync(() => firstDisposal.WaitAsync(CompletionBound));
        Assert.NotNull(firstFailure);
        Assert.Contains("cancellation callback failed", firstFailure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("release cleanup failed", firstFailure.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, executor.ReleaseCount);

        var repeatedFailure = await Record.ExceptionAsync(() => executor.DisposeAsync().AsTask());
        Assert.NotNull(repeatedFailure);
        Assert.Contains("cancellation callback failed", repeatedFailure.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, executor.ReleaseCount);
    }

    private sealed class FaultingExecutor : ReadOnlySqlExecutorBase
    {
        private int _releaseCount;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReleaseCount => Volatile.Read(ref _releaseCount);

        protected override async Task<SqlReadResult> ExecuteCoreAsync(SqlReadRequest request, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                CancellationObserved.TrySetResult();
                throw new InvalidOperationException("cancellation callback failed");
            });
            Started.TrySetResult();
            await AllowCompletion.Task.ConfigureAwait(false);
            return new([], [], false, []);
        }

        protected override ValueTask ReleaseResourcesAsync()
        {
            Interlocked.Increment(ref _releaseCount);
            throw new InvalidOperationException("release cleanup failed");
        }
    }
}
