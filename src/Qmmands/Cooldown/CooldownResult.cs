using System;
using System.Threading.Tasks;

namespace Qmmands
{
    public class CooldownResult : IResult
    {
        private CooldownResult(TimeSpan? retryAfter)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan? RetryAfter { get; }

        public bool IsSuccessful => !RetryAfter.HasValue;

        public static CooldownResult OnCooldown(TimeSpan retryAfter) => new CooldownResult(retryAfter);

        public static CooldownResult NotOnCooldown => new CooldownResult(default);

        public static implicit operator ValueTask<CooldownResult>(CooldownResult result)
            => new ValueTask<CooldownResult>(result);
    }
}