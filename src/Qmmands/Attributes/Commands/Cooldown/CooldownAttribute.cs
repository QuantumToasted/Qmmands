using System;

namespace Qmmands
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public abstract class CooldownAttribute : Attribute
    {
        public int Amount { get; }

        public TimeSpan Per { get; }

        protected CooldownAttribute(int amount, double per, CooldownMeasure cooldownMeasure)
        {
            Amount = amount;
            Per = cooldownMeasure switch
            {
                CooldownMeasure.Milliseconds => TimeSpan.FromMilliseconds(per),
                CooldownMeasure.Seconds => TimeSpan.FromSeconds(per),
                CooldownMeasure.Minutes => TimeSpan.FromMinutes(per),
                CooldownMeasure.Hours => TimeSpan.FromHours(per),
                CooldownMeasure.Days => TimeSpan.FromDays(per),
                _ => throw new ArgumentOutOfRangeException(nameof(cooldownMeasure), "Invalid cooldown measure."),
            };
        }
    }
}
