using System;

namespace TriLink.Core
{
    /// <summary>
    /// Small circuit breaker for periodic work. The counter is consecutive:
    /// one successful run clears earlier transient failures.
    /// </summary>
    public sealed class ConsecutiveFailureLimiter
    {
        public ConsecutiveFailureLimiter(int failureLimit)
        {
            if (failureLimit < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(failureLimit),
                    "Failure limit must be at least one.");
            }

            FailureLimit = failureLimit;
        }

        public int FailureLimit { get; private set; }

        public int ConsecutiveFailures { get; private set; }

        public bool IsTripped { get; private set; }

        public bool RecordFailure()
        {
            if (!IsTripped)
            {
                ConsecutiveFailures = Math.Min(
                    FailureLimit,
                    ConsecutiveFailures + 1);
                IsTripped = ConsecutiveFailures >= FailureLimit;
            }

            return IsTripped;
        }

        public bool RecordSuccess()
        {
            var recovered = ConsecutiveFailures > 0 || IsTripped;
            Reset();
            return recovered;
        }

        public void Reset()
        {
            ConsecutiveFailures = 0;
            IsTripped = false;
        }
    }
}
