using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Qmmands
{
    /// <summary>
    ///     Represents a command built using the <see cref="CommandService"/>.
    /// </summary>
    public sealed class Command
    {
        /// <summary>
        ///     Gets the name of this <see cref="Command"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets the description of this <see cref="Command"/>.
        /// </summary>
        public string Description { get; }

        /// <summary>
        ///     Gets the remarks of this <see cref="Command"/>.
        /// </summary>
        public string Remarks { get; }

        /// <summary>
        ///     Gets the priority of this <see cref="Command"/>.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        ///     Gets the <see cref="Qmmands.RunMode"/> of this <see cref="Command"/>.
        /// </summary>
        public RunMode RunMode { get; }

        /// <summary>
        ///     Gets whether this <see cref="Command"/> ignores extra arguments or not.
        /// </summary>
        public bool IgnoresExtraArguments { get; }

        /// <summary>
        ///     Gets or sets the <see cref="Type"/> of a custom <see cref="IArgumentParser"/>.
        /// </summary>
        public Type CustomArgumentParserType { get; }

        /// <summary>
        ///     Gets the aliases of this <see cref="Command"/>.
        /// </summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>
        ///     Gets the full aliases of this <see cref="Command"/>.
        /// </summary>
        /// <remarks>
        ///     Aliases of parent <see cref="Qmmands.Module"/>s and this <see cref="Command"/>
        ///     concatenated using the <see cref="CommandService.Separator"/>.
        /// </remarks>
        public IReadOnlyList<string> FullAliases { get; }

        /// <summary>
        ///     Gets the checks of this <see cref="Command"/>.
        /// </summary>
        public IReadOnlyList<CheckAttribute> Checks { get; }

        /// <summary>
        ///     Gets the attributes of this <see cref="Command"/>.
        /// </summary>
        public IReadOnlyList<Attribute> Attributes { get; }

        /// <summary>
        ///     Gets the parameters of this <see cref="Command"/>.
        /// </summary>
        public IReadOnlyList<Parameter> Parameters { get; }

        /// <summary>
        ///     Gets whether this <see cref="Command"/> is enabled or not.
        /// </summary>
        public bool IsEnabled => Module.IsEnabled && Volatile.Read(ref _isEnabled);

        /// <summary>
        ///     Gets the <see cref="Qmmands.Module"/> of this <see cref="Command"/>.
        /// </summary>
        public Module Module { get; }

        /// <summary>
        ///     Gets the <see cref="CommandService"/> of this <see cref="Command"/>.
        /// </summary>
        public CommandService Service => Module.Service;

        internal readonly object Callback;

        internal readonly (bool HasRemainder, string Identifier) SignatureIdentifier;

        private bool _isEnabled;

        internal Command(CommandBuilder builder, Module module)
        {
            Module = module;

            Description = builder.Description;
            Remarks = builder.Remarks;
            Priority = builder.Priority;
            RunMode = builder.RunMode ?? module.RunMode;
            IgnoresExtraArguments = builder.IgnoresExtraArguments ?? module.IgnoresExtraArguments;
            CustomArgumentParserType = builder.CustomArgumentParserType ?? module.CustomArgumentParserType;
            Callback = builder.Callback;
            var aliases = builder.Aliases.ToImmutableArray();
            Aliases = aliases;

            var fullAliases = ImmutableArray.CreateBuilder<string>();
            if (Module.FullAliases.Count == 0)
            {
                fullAliases.AddRange(aliases);
            }
            else if (aliases.Length == 0)
            {
                fullAliases.AddRange((ImmutableArray<string>) Module.FullAliases);
            }
            else
            {
                for (var i = 0; i < Module.FullAliases.Count; i++)
                {
                    var moduleAlias = Module.FullAliases[i];
                    var absolute = moduleAlias.Length == 0;
                    for (var j = 0; j < aliases.Length; j++)
                    {
                        var alias = aliases[j];
                        if (alias.Length == 0)
                        {
                            if (absolute)
                                continue;

                            fullAliases.Add(moduleAlias);
                        }
                        else if (absolute)
                        {
                            fullAliases.Add(alias);
                        }
                        else
                        {
                            fullAliases.Add(string.Concat(moduleAlias, Service.Separator, alias));
                        }
                    }
                }
            }
            FullAliases = fullAliases.TryMoveToImmutable();

            Name = builder.Name ?? (FullAliases.Count > 0 ? FullAliases[0] : null);

            for (var i = 0; i < builder.Checks.Count; i++)
                builder.Checks[i].Command = this;
            Checks = builder.Checks.ToImmutableArray();
            Attributes = builder.Attributes.ToImmutableArray();

            var hasRemainder = false;
            var sb = new StringBuilder();
            var parameters = ImmutableArray.CreateBuilder<Parameter>(builder.Parameters.Count);
            for (var i = 0; i < builder.Parameters.Count; i++)
            {
                var parameter = builder.Parameters[i].Build(this);
                parameters.Add(parameter);

                if (parameter.IsRemainder)
                    hasRemainder = true;

                sb.Append(parameter.Type).Append(';');
            }
            Parameters = parameters.TryMoveToImmutable();
            SignatureIdentifier = (hasRemainder, sb.ToString());

            _isEnabled = builder.IsEnabled;
        }

        /// <summary>
        ///     Runs checks on parent <see cref="Qmmands.Module"/>s and this <see cref="Command"/>.
        /// </summary>
        /// <param name="context"> The <see cref="CommandContext"/> used for execution. </param>
        /// <returns>
        ///     A <see cref="SuccessfulResult"/> if all of the checks pass, otherwise a <see cref="ChecksFailedResult"/>.
        /// </returns>
        public async Task<IResult> RunChecksAsync(CommandContext context)
        {
            var result = await Module.RunChecksAsync(context).ConfigureAwait(false);
            if (!result.IsSuccessful)
                return result;

            if (Checks.Count > 0)
            {
                async Task<(CheckAttribute Check, CheckResult Result)> RunCheckAsync(CheckAttribute check)
                {
                    var checkResult = await check.CheckAsync(context).ConfigureAwait(false);
                    return (check, checkResult);
                }

                var checkResults = await Task.WhenAll(Checks.Select(RunCheckAsync)).ConfigureAwait(false);
                var failedGroups = checkResults.GroupBy(x => x.Check.Group)
                    .Where(x => x.Key == null ? x.Any(y => !y.Result.IsSuccessful) : x.All(y => !y.Result.IsSuccessful)).ToImmutableArray();
                if (failedGroups.Length > 0)
                    return new ChecksFailedResult(this, failedGroups.SelectMany(x => x).Where(x => !x.Result.IsSuccessful).ToImmutableArray());
            }

            return new SuccessfulResult();
        }

        public async Task<IResult> RunCooldownsAsync(CommandContext context)
        {
            if (Service.CooldownProvider != null)
            {
                // TODO: Something other than list, i'm sure you'll complain.
                var cooldowns = Attributes.OfType<CooldownAttribute>().ToList();

                if (cooldowns.Count > 0)
                {
                    async Task<(CooldownAttribute Cooldown, CooldownResult Result)> RunCooldownsAsync(
                        CooldownAttribute cooldown)
                    {
                        var cooldownResult = await Service.CooldownProvider.CheckCooldownAsync(cooldown, context)
                            .ConfigureAwait(false);
                        return (cooldown, cooldownResult);
                    }

                    var cooldownResults = await Task.WhenAll(cooldowns.Select(RunCooldownsAsync)).ConfigureAwait(false);
                    var failedCooldowns = cooldownResults.Where(x => !x.Result.IsSuccessful).Select(x =>
                        (x.Cooldown, x.Result.RetryAfter.Value)).ToImmutableArray();
                    if (failedCooldowns.Length > 0)
                        return new CommandOnCooldownResult(this, failedCooldowns.ToImmutableArray());
                }
            }

            return new SuccessfulResult();
        }

        /// <summary>
        ///     Attempts to parse the raw arguments for this <see cref="Command"/> and execute it.
        ///     Short for <see cref="CommandService.ExecuteAsync(Command, string, CommandContext)"/>
        /// </summary>
        /// <param name="rawArguments"> The raw arguments to use for this <see cref="Command"/>'s <see cref="Parameter"/>s. </param>
        /// <param name="context"> The <see cref="CommandContext"/> to use during execution. </param>
        /// <returns>
        ///     An <see cref="IResult"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The command must not be null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     The raw arguments must not be null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     The context must not be null.
        /// </exception>
        public Task<IResult> ExecuteAsync(string rawArguments, CommandContext context)
            => Service.ExecuteAsync(this, rawArguments, context);

        /// <summary>
        ///     Executes this <see cref="Command"/>.
        /// </summary>
        /// <param name="arguments"> The parsed arguments to use for this <see cref="Command"/>'s <see cref="Parameter"/>s. </param>
        /// <param name="context"> The <see cref="CommandContext"/> to use during execution. </param>
        /// <returns>
        ///     An <see cref="IResult"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The command must not be null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     The raw arguments must not be null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     The context must not be null.
        /// </exception>
        public Task<IResult> ExecuteAsync(IEnumerable<object> arguments, CommandContext context)
            => Service.ExecuteAsync(this, arguments, context);

        /// <summary>
        ///     Enables this <see cref="Command"/>.
        /// </summary>
        public void Enable()
            => Volatile.Write(ref _isEnabled, true);

        /// <summary>
        ///     Disables this <see cref="Command"/>.
        /// </summary>
        public void Disable()
            => Volatile.Write(ref _isEnabled, false);

        /// <summary>
        ///     Returns <see cref="Name"/> or calls <see cref="object.ToString"/> if it is <see langword="null"/>.
        /// </summary>
        /// <returns>
        ///     A <see cref="string"/> representing this <see cref="Command"/>.
        /// </returns>
        public override string ToString()
            => Name ?? base.ToString();
    }
}