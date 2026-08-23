using System.Threading.Tasks;

namespace MemeSearcher.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
