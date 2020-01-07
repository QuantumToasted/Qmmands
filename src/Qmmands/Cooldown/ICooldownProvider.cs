using System.Threading.Tasks;

namespace Qmmands
{
    public interface ICooldownProvider
    {
        ValueTask<CooldownResult> CheckCooldownAsync(CooldownAttribute cooldown, CommandContext context);
    }
}