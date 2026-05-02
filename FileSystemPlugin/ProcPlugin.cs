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
            try
            {
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


        [KernelFunction("KillProcess"), Description("Kills a process by name.")]
        public async Task<object> KillProcessAsync(
            [Description("The name of the process to kill (e.g., 'notepad')")] string processName)
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcesses()
                    .Where(s => s.ProcessName.Contains(processName, StringComparison.InvariantCultureIgnoreCase))
                    .ToArray();
                if (processes.Length == 0)
                {
                    return new
                    {
                        Success = false,
                        Message = $"No processes found with name '{processName}'."
                    };
                }
                List<string> killedProcesses = new List<string>();
                List<string> failedProcesses = new List<string>();
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        killedProcesses.Add(processName);
                    }
                    catch
                    {
                        failedProcesses.Add(processName);
                    }
                }

                if (!killedProcesses.Any())
                {
                    return new
                    {
                        Success = false,
                        Message = $"Failed to kill any instances of '{processName}'."
                    };
                }

                return new
                {
                    Success = true,
                    Message = $"Killed {killedProcesses.Count} instance(s) of '{processName}'.",
                    KilledProcesses = killedProcesses,
                    FailedToKillProcesses = failedProcesses
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    Success = false,
                    Message = $"Failed to kill process: {ex.Message}"
                };
            }
        }
    }
}
