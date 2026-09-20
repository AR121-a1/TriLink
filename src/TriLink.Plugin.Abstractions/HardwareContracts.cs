using System.Threading.Tasks;
using TriLink.Plugin;

namespace TriLink.Core
{
    // Additive contract: all discovery/search/business I/O shares one adapter owner.
    [PluginService("trilink.hardware-commands")]
    public interface IHardwareCommandService
    {
        Task<string> ExecuteAsync(string portName, string command, string arguments);
    }
}
