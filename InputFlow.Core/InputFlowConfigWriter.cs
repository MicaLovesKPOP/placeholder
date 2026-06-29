using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace InputFlow.Core
{
    public sealed class InputFlowConfigSaveResult
    {
        private InputFlowConfigSaveResult(bool success, IReadOnlyList<string> errors)
        {
            Success = success;
            Errors = errors;
        }

        public bool Success { get; }
        public IReadOnlyList<string> Errors { get; }

        public static InputFlowConfigSaveResult Saved()
        {
            return new InputFlowConfigSaveResult(true, Array.Empty<string>());
        }

        public static InputFlowConfigSaveResult Failed(IReadOnlyList<string> errors)
        {
            return new InputFlowConfigSaveResult(false, errors.Count == 0 ? new[] { "Config save failed." } : errors);
        }

        public static InputFlowConfigSaveResult Failed(string error)
        {
            return new InputFlowConfigSaveResult(false, new[] { error });
        }
    }

    public static class InputFlowConfigWriter
    {
        private const string LastKnownGoodSuffix = ".last-good";

        public static InputFlowConfigSaveResult SaveValidated(InputFlowConfig config, string path)
        {
            string? tempPath = null;
            string? lastKnownGoodTempPath = null;

            if (config == null)
            {
                return InputFlowConfigSaveResult.Failed("Config must not be null.");
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return InputFlowConfigSaveResult.Failed("Config path must not be empty.");
            }

            var errors = InputFlowConfigValidator.Validate(config);
            if (errors.Count > 0)
            {
                return InputFlowConfigSaveResult.Failed(errors);
            }

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(config, CreateJsonOptions());
                tempPath = CreateTempPath(path);
                lastKnownGoodTempPath = CreateTempPath(GetLastKnownGoodPath(path));
                File.WriteAllText(tempPath, json);
                File.WriteAllText(lastKnownGoodTempPath, json);
                MoveReplacing(tempPath, path);
                MoveReplacing(lastKnownGoodTempPath, GetLastKnownGoodPath(path));
                return InputFlowConfigSaveResult.Saved();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return InputFlowConfigSaveResult.Failed($"Could not save config: {ex.Message}");
            }
            finally
            {
                DeleteIfExists(tempPath);
                DeleteIfExists(lastKnownGoodTempPath);
            }
        }

        public static string GetLastKnownGoodPath(string path)
        {
            return $"{path}{LastKnownGoodSuffix}";
        }

        private static string CreateTempPath(string path)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string fileName = Path.GetFileName(path);
            return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        }

        private static void MoveReplacing(string sourcePath, string destinationPath)
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
        }

        private static void DeleteIfExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }
    }
}
