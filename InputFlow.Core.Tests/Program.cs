using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    ("UnsupportedWorkflowModeIsRejected", UnsupportedWorkflowModeIsRejected),
    ("ProfileMatchReportsExplainCompatibilityFallback", ProfileMatchReportsExplainCompatibilityFallback),
    ("ProfileMatchReportsExplainCandidateFailures", ProfileMatchReportsExplainCandidateFailures),
    ("DiagnosticsReportIncludesProfileInventoryAndMatches", DiagnosticsReportIncludesProfileInventoryAndMatches),
    ("FirstRunConfigUsesInstalledProfiles", FirstRunConfigUsesInstalledProfiles),
    ("FirstRunConfigHandlesUnknownLanguageTags", FirstRunConfigHandlesUnknownLanguageTags)
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
        { "Id": "us-intl", "Match": { "LanguageTag": "nl-NL" } },
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

static void ProfileMatchReportsExplainCompatibilityFallback()
{
    var installed = CreateInstalledProfiles();
    var definitions = new[]
    {
        new ProfileDefinition
        {
            Id = "us-intl",
            Match = new ProfileMatch { LanguageTag = "en-NL" }
        }
    };

    var report = InputProfileManager.EvaluateProfileMatches(installed, definitions).Single();

    AssertTrue(report.IsMatch, "en-NL should match US-International compatibility fallback.");
    AssertTrue(report.UsedCompatibilityFallback, "Expected compatibility fallback to be reported.");
    AssertEqual("00020409", report.MatchedProfile!.KLID, "Fallback should select US-International KLID.");
    AssertContains(new[] { report.Summary }, "compatibility fallback");
}

static void ProfileMatchReportsExplainCandidateFailures()
{
    var installed = CreateInstalledProfiles();
    var definitions = new[]
    {
        new ProfileDefinition
        {
            Id = "missing",
            Match = new ProfileMatch { LanguageTag = "ja-JP", KLID = "00000411" }
        }
    };

    var report = InputProfileManager.EvaluateProfileMatches(installed, definitions).Single();

    AssertFalse(report.IsMatch, "ja-JP profile should not match the fake installed set.");
    AssertTrue(report.Candidates.Count > 0, "Expected candidate diagnostics.");
    AssertContains(report.Candidates.Select(candidate => candidate.Reason), "LanguageTag expected ja-JP");
}

static void DiagnosticsReportIncludesProfileInventoryAndMatches()
{
    var config = CreateKnownWorkingWorkflowConfig();
    var report = InputFlowDiagnostics.BuildReport(config, CreateInstalledProfiles(), "C:\\InputFlow\\inputflow.json", "C:\\InputFlow\\inputflow.log");

    AssertContains(new[] { report }, "InputFlow diagnostics");
    AssertContains(new[] { report }, "Configured workflows: 1");
    AssertContains(new[] { report }, "Installed input profiles: 3");
    AssertContains(new[] { report }, "Configured profile match reports: 2");
    AssertContains(new[] { report }, "F0010413");
    AssertContains(new[] { report }, "Dutch (Netherlands)");
}

static void FirstRunConfigUsesInstalledProfiles()
{
    var config = InputFlowConfigFactory.CreateFirstRunConfig(CreateInstalledProfiles());
    var errors = InputFlowConfigValidator.Validate(config);

    AssertEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    AssertEqual(3, config.Profiles.Count, "First-run config should define one profile per installed profile.");
    AssertEqual(0, config.Workflows.Count, "First-run config should not guess a user's workflow.");
    AssertTrue(config.Profiles.Any(profile => profile.Id == "nl-nl"), "Expected Dutch profile id to come from language tag.");
    AssertTrue(config.Profiles.Any(profile => profile.Id == "en-us"), "Expected English profile id to come from language tag.");

    var korean = config.Profiles.Single(profile => profile.Id == "ko-kr");
    AssertEqual("hangul", korean.EnterMode, "Korean should opt into the safe Hangul enter-mode adapter.");
    AssertEqual("E0010412", korean.Match.KLID, "First-run profile matching should include exact KLID.");
}

static void FirstRunConfigHandlesUnknownLanguageTags()
{
    var installed = new[]
    {
        new InputProfile(new IntPtr(0x00001234), "00001234", "Custom Layout", isIme: false)
    };

    var config = InputFlowConfigFactory.CreateFirstRunConfig(installed);
    var errors = InputFlowConfigValidator.Validate(config);

    AssertEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    AssertEqual(1, config.Profiles.Count, "Expected one generated profile.");
    AssertEqual("00001234", config.Profiles[0].Match.KLID, "Unknown language profiles should still match by KLID.");
    AssertEqual(null, config.Profiles[0].Match.LanguageTag, "Unknown language profiles should not emit invalid language tags.");
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
                Match = new ProfileMatch { LanguageTag = "nl-NL" }
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

static IReadOnlyList<InputProfile> CreateInstalledProfiles()
{
    return new[]
    {
        new InputProfile(new IntPtr(unchecked((int)0xF0010413)), "F0010413", "Dutch (Netherlands)", isIme: true, languageTag: "nl-NL"),
        new InputProfile(new IntPtr(0x00020409), "00020409", "English (United States)", isIme: true, languageTag: "en-US"),
        new InputProfile(new IntPtr(0xE0010412L), "E0010412", "Korean", isIme: true, languageTag: "ko-KR")
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
