using System;
using System.Collections.Generic;
using System.Linq;

namespace Qmmands
{
    /// <summary>
    ///     Represents the command being on a cooldown.
    /// </summary>
    public sealed class CommandOnCooldownResult : FailedResult
    {
        /// <summary>
        ///     Gets the reason of this failed result.
        /// </summary>
        public override string Reason => _lazyReason.Value;
        private readonly Lazy<string> _lazyReason;

        /// <summary>
        ///     Gets the <see cref="Qmmands.Command"/> that is on cooldown.
        /// </summary>
        public Command Command { get; }

        /// <summary>
        ///     Gets the <see cref="Cooldown"/>s and <see cref="TimeSpan"/>s after which it is safe to retry.
        /// </summary>
        public IReadOnlyList<(CooldownAttribute Cooldown, TimeSpan RetryAfter)> Cooldowns { get; }

        internal CommandOnCooldownResult(Command command, IReadOnlyList<(CooldownAttribute Cooldown, TimeSpan RetryAfter)> cooldowns)
        {
            Command = command;
            Cooldowns = cooldowns;

            _lazyReason = new Lazy<string>(() =>
            {
                return cooldowns.Count == 1
                    ? $"Command {command} is on cooldown. Retry after {cooldowns[0].RetryAfter}." // TODO: Whatever you want to be displayed.
                    : $"Command {command} is on multiple cooldowns: {string.Join(", ", cooldowns.Select(x => $"'{x.Cooldown.GetType().Name}' - retry after {x.RetryAfter}"))}";
            }, true);
        }
    }
}
