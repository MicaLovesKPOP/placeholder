using System;
using System.Collections.Generic;
using System.IO;
using InputFlow.Core;

var tests = new (string Name, Action Test)[]
{
    ("CurrentWorkflowConfigIsValid", CurrentWorkflowConfigIsValid),
    ("LegacyV1HotkeysMigrateToWorkflows", LegacyV1HotkeysMigrateToWorkflows),
    ("InvalidWorkflowProfileReferenceIsRejected", InvalidWorkflowProfileReferenceIsRejected),
    ("CycleWorkflowRequiresTwoTargets", CycleWorkflowRequiresTwoTargets),
    ("ThreeProfileCycleConfigIsValid", ThreeProfileCycleConfigIsValid),
    ("ProfilesRequireMatchCriteria", ProfilesRequireMatchCriteria),
    ("MalformedJsonReturnsLoadErrors", MalformedJsonReturnsLoadErrors),
    ("LegacyLoadFallsBackToDefaultsOnInvalidConfig", LegacyLoadFallsBackToDefaultsOnInvalidConfig),
    ("NullCollectionsAreNormalized", NullCollectionsAreNormalized),
    ("UnsupportedWorkflowModeIsRejected", UnsupportedWorkflowModeIsRejected)
};

int failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Test();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static void CurrentWorkflowConfigIsValid()
{
    var config = CreateKnownWorkingWorkflowConfig();
    var errors = InputFlowConfigValidator.Validate(config);
    AssertEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
}

static void LegacyV1HotkeysMigrateToWorkflows()
{
    string path = WriteTempConfig("""
    {
      "Version": 1,
      "Startup": false,
      "ShowTrayIcon": true,
      "LogLevel": "Info",
      "Hotkeys": [
        {
          "Name": "Korean toggle",
          "Keys": "RightAlt",
          "Mode": "toggle",
          "Target": "korean",
          "ReturnBehavior": "alwaysSpecificLayout",
          "Fallback": "us-intl"
        }
      ],
      "Profiles": [
        { "Id": "us-intl", "Match": { "LanguageTag": "en-NL" } },
        { "Id": "korean", "Match": { "LanguageTag": "ko-KR" }, "EnterMode": "hangul" }
      ]
    }
    """);

    try
    {
        var result = InputFlowConfig.LoadDetailed(path);

        AssertTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
        AssertEqual(InputFlowConfig.CurrentVersion, result.Config.Version, "Legacy config should migrate to current version.");
        AssertEqual(1, result.Config.Workflows.Count, "Legacy hotkey should become one workflow.");
        AssertEqual("toggle", result.Config.Workflows[0].Mode, "Migrated workflow should preserve mode.");
        AssertEqual("RightAlt", result.Config.Workflows[0].Triggers[0].Keys, "Migrated workflow should preserve trigger.");
        AssertEqual("korean", result.Config.Workflows[0].Target, "Migrated workflow should preserve target.");
    }
    finally
    {
        File.Delete(path);
    }
}

static void InvalidWorkflowProfileReferenceIsRejected()
{
    var config = CreateKnownWorkingWorkflowConfig();
    config.Workflows[0].Fallback = "missing";

    var errors = InputFlowConfigValidator.Validate(config);

    AssertContains(errors, "Fallback references unknown profile 'missing'");
}

static void CycleWorkflowRequiresTwoTargets()
{
    var config = CreateKnownWorkingWorkflowConfig();
    config.Workflows[0] = new WorkflowConfig
    {
        Id = "bad-cycle",
        Name = "Bad cycle",
        Mode = "cycle",
        Triggers = new List<TriggerConfig> { new TriggerConfig { Keys = "F13" } },
        Targets = new List<string> { "us-intl" }
    };

    var errors = InputFlowConfigValidator.Validate(config);

    AssertContains(errors, "Targets must contain at least two profiles for cycle mode");
}

static void ThreeProfileCycleConfigIsValid()
{
    var config = CreateKnownWorkingWorkflowConfig();
    config.Profiles.Add(new ProfileDefinition
    {
        Id = "japanese",
        Match = new ProfileMatch { LanguageTag = "ja-JP" }
    });
    config.Workflows.Add(new WorkflowConfig
    {
        Id = "writing-cycle",
        Name = "Writing cycle",
        Mode = "cycle",
        Triggers = new List<TriggerConfig> { new TriggerConfig { Keys = "Ctrl+Shift+Space" } },
        Targets = new List<string> { "us-intl", "korean", "japanese" }
    });

    var errors = InputFlowConfigValidator.Validate(config);

    AssertEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
}

static void ProfilesRequireMatchCriteria()
{
    var config = CreateKnownWorkingWorkflowConfig();
    config.Profiles.Add(new ProfileDefinition
    {
        Id = "empty",
        Match = new ProfileMatch()
    });

    var errors = InputFlowConfigValidator.Validate(config);

    AssertContains(errors, "Match must define at least one criterion");
}

static void MalformedJsonReturnsLoadErrors()
{
    string path = WriteTempConfig("{ not json }");
    try
    {
        var result = InputFlowConfig.LoadDetailed(path);

        AssertFalse(result.Success, "Malformed JSON should fail detailed config loading.");
        AssertContains(result.Errors, "Could not read or parse config");
    }
    finally
    {
        File.Delete(path);
    }
}

static void LegacyLoadFallsBackToDefaultsOnInvalidConfig()
{
    string path = WriteTempConfig("{ \"Version\": 999 }");
    try
    {
        var config = InputFlowConfig.Load(path);

        AssertEqual(InputFlowConfig.CurrentVersion, config.Version, "Legacy Load should return defaults after invalid config.");
        AssertEqual(0, config.Workflows.Count, "Default config should not use invalid workflows.");
    }
    finally
    {
        File.Delete(path);
    }
}

static void NullCollectionsAreNormalized()
{
    string path = WriteTempConfig("{ \"Version\": 2, \"Hotkeys\": null, \"Workflows\": null, \"Profiles\": null, \"ExcludedProcesses\": null }");
    try
    {
        var result = InputFlowConfig.LoadDetailed(path);

        AssertTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
        AssertEqual(0, result.Config.Hotkeys.Count, "Hotkeys should be normalized.");
        AssertEqual(0, result.Config.Workflows.Count, "Workflows should be normalized.");
        AssertEqual(0, result.Config.Profiles.Count, "Profiles should be normalized.");
        AssertEqual(0, result.Config.ExcludedProcesses.Count, "Excluded processes should be normalized.");
    }
    finally
    {
        File.Delete(path);
    }
}

static void UnsupportedWorkflowModeIsRejected()
{
    var config = CreateKnownWorkingWorkflowConfig();
    config.Workflows[0].Mode = "hold";

    var errors = InputFlowConfigValidator.Validate(config);

    AssertContains(errors, "Mode 'hold' is not supported");
}

static InputFlowConfig CreateKnownWorkingWorkflowConfig()
{
    return new InputFlowConfig
    {
        Version = InputFlowConfig.CurrentVersion,
        Startup = false,
        ShowTrayIcon = true,
        LogLevel = "Info",
        ExcludedProcesses = new List<string> { "mstsc.exe", "vmconnect.exe" },
        Profiles = new List<ProfileDefinition>
        {
            new ProfileDefinition
            {
                Id = "us-intl",
                Match = new ProfileMatch { LanguageTag = "en-NL" }
            },
            new ProfileDefinition
            {
                Id = "korean",
                Match = new ProfileMatch { LanguageTag = "ko-KR" },
                EnterMode = "hangul"
            }
        },
        Workflows = new List<WorkflowConfig>
        {
            new WorkflowConfig
            {
                Id = "korean-toggle",
                Name = "Korean toggle",
                Mode = "toggle",
                Triggers = new List<TriggerConfig>
                {
                    new TriggerConfig { Keys = "RightAlt" }
                },
                Target = "korean",
                ReturnBehavior = "alwaysSpecificLayout",
                Fallback = "us-intl"
            }
        }
    };
}

static string WriteTempConfig(string content)
{
    string path = Path.Combine(Path.GetTempPath(), $"inputflow-test-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, content);
    return path;
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'. {message}");
    }
}

static void AssertFalse(bool value, string message)
{
    if (value)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertTrue(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertContains(IEnumerable<string> values, string expectedSubstring)
{
    foreach (string value in values)
    {
        if (value.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    throw new InvalidOperationException($"Expected to find '{expectedSubstring}'. Actual: {string.Join(" | ", values)}");
}
