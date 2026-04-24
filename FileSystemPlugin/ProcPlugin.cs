using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Transactions;

namespace FileSystemPlugin
{
    public class ProcPlugin
    {
        [KernelFunction("RestartExplorer"), Description("Restarts the Windows Explorer process.")]
        public async Task<object> RestartExplorerAsync()
        {
            try {
                var explorerProcesses = System.Diagnostics.Process.GetProcessesByName("explorer");
                foreach (var process in explorerProcesses)
                {
                    process.Kill();
                }

                System.Diagnostics.Process.Start("explorer.exe");
                return new
                {
                    Success = true,
                    Message = "Explorer restarted successfully."
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    Success = false,
                    Message = $"Failed to restart explorer: {ex.Message}"
                };
            }
        }
    }
}
