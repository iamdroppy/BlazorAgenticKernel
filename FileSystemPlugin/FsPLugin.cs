using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace FileSystemPlugin
{
    public sealed class FsPLugin
    {
        private string HomeDir(string path)
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path.StartsWith("~"))
            {
                path = Path.Combine(homeDirectory, path.Substring(1).TrimStart(Path.DirectorySeparatorChar));
            }
            return path;
        }
        [KernelFunction("DirectoryList"), Description("List folders/files of existing directory")]
        public async Task<object> DirectoryListAsync(
            [Description("The path of the directory to list (use ~ for home directory)")] string path)
        {
            path = HomeDir(path);
            if (File.Exists(path))
            {
                return new
                {
                    Success = false,
                    Message = $"'{path}' is a file, not a directory."
                };
            }
            if (!Directory.Exists(path))
                return new
                {
                    Success = false,
                    Message = $"Directory '{path}' does not exist."
                };

            var entries = Directory.EnumerateFileSystemEntries(path)
                .Select(entry => new
                {
                    Name = Path.GetFileName(entry),
                    IsDirectory = Directory.Exists(entry)
                })
                .ToList();

            return new
            {
                Success = true,
                Entries = entries
            };
        }

        [KernelFunction("CreateDirectory"), Description("Creates a directory if it doesn't exist.")]
        public async Task<object> CreateDirectoryAsync(
            [Description("The path of the directory to create (use ~ for home directory)")] string path)
        {
            path = HomeDir(path);
            if (Directory.Exists(path))
            {
                return new
                {
                    Success = false,
                    Message = $"'{path}' is a file, not a directory."
                };
            }
            if (Directory.Exists(path))
                return new
                {
                    Success = false,
                    Message = $"Directory '{path}' already exists."
                };

            Directory.CreateDirectory(path);

            return new
            {
                Success = true,
                Entries = $"Successfully created path: {path}"
            };
        }

        [KernelFunction("OpenCode"), Description("Opens the path with Visual Studio Code")]
        public async Task<object> OpenCodeAsync(
            [Description("The path of the file or directory to open with Visual Studio Code (use ~ for home directory)")] string path)
        {
            path = HomeDir(path);
            if (!File.Exists(path) && !Directory.Exists(path))
                return new
                {
                    Success = false,
                    Message = $"Path '{path}' does not exist."
                };
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "code";
                process.StartInfo.Arguments = $"\"{path}\"";
                process.StartInfo.UseShellExecute = true;
                process.Start();
                return new
                {
                    Success = true,
                    Message = $"Opened '{path}' in Visual Studio Code."
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    Success = false,
                    Message = $"Failed to open in Visual Studio Code: {ex.Message}"
                };
            }
        }
        [KernelFunction("DeleteFileOrDir"), Description("Deletes a file or a folder")]
        public async Task<object> DeleteAsync(
            [Description("The full path for the file or directory (use ~ for the home directory)")] string pathToFileOrDir)
        {
            pathToFileOrDir = HomeDir(pathToFileOrDir);
            if (File.Exists(pathToFileOrDir))
            {
                File.Delete(pathToFileOrDir);
                return new
                {
                    Success = true,
                    FileDeleted = pathToFileOrDir
                };
            }
            else if (Directory.Exists(pathToFileOrDir))
            {
                Directory.Delete(pathToFileOrDir, true);
                return new
                {
                    Success = true,
                    Directory = pathToFileOrDir
                };
            }
            else
            {
                return new
                {
                    Success = false,
                    Message = $"File or directory not found in {pathToFileOrDir}"
                };
            }
        }
        [KernelFunction("ReadFile"), Description("Reads the content of a file at the specified path.")]
        public async Task<object> ReadFileAsync(
            [Description("The path of the file to read (use ~ for home directory)")] string filePath)
        {
            filePath = HomeDir(filePath);

            try
            {
                string content = await File.ReadAllTextAsync(filePath);
                return new
                {
                    Success = true,
                    Content = content
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    Success = false,
                    Message = $"Failed to read file: {ex.Message}"
                };
            }
        }

        [KernelFunction("WriteFile"), Description("Writes the specified content to a file at the given path.")]
        public async Task<object> WriteFileAsync(
            [Description("The path of the file to write (use ~ for home directory)")] string filePath, 
            [Description("The new content of the file")] string content)
        {
            try
            {
                filePath = HomeDir(filePath);

                await File.WriteAllTextAsync(filePath, content);
                return new
                {
                    Success = true,
                    Message = "File written successfully."
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    Success = false,
                    Message = $"Failed to write file: {ex.Message}"
                };
            }
        }
    }
}
