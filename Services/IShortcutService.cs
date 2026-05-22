using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

public interface IShortcutService
{
    Task ShowShortcutHelpAsync(CancellationToken cancellationToken = default);
}
