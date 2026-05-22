using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

public interface IDialogService
{
    Task<DialogResultKind> ShowAsync(DialogRequest request, CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default);

    Task<string?> PromptTextAsync(TextInputDialogRequest request, CancellationToken cancellationToken = default);

    Task<DialogResultKind> ShowContentAsync(ContentDialogRequest request, CancellationToken cancellationToken = default);

    Task<T?> SelectAsync<T>(SelectionDialogRequest<T> request, CancellationToken cancellationToken = default);
}
