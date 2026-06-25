using System;
using System.Collections.Generic;
using System.IO;
using InputFlow.Core;

var tests = new (string Name, Action Test)[]
{
    ("CurrentSampleConfigIsValid", CurrentSampleConfigIsValid),
    ("InvalidProfileReferenceIsRejected", InvalidProfileReferenceIsRejected),
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

static void CurrentSampleConfigIsValid()
{
    var config = CreateKnownWorkingConfig();
    var errors = InputFlowConfigValidator.Validate(config);
    AssertEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
}

static void InvalidProfileReferenceIsRejected()
{
    var config = CreateKnownWorkingConfig();
    config.Hotkeys[0].Fallback = "missing";

    var errors = InputFlowConfigValidator.Validate(config);

    AssertContains(errors, "Fallback references unknown profile 'missing'");
}

static void ProfilesRequireMatchCriteria()
{
    var config = CreateKnownWorkingConfig();
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

        AssertEqual(1, config.Version, "Legacy Load should return defaults after invalid config.");
        AssertEqual(0, config.Hotkeys.Count, "Default config should not use invalid hotkeys.");
    }
    finally
    {
        File.Delete(path);
    }
}

static void NullCollectionsAreNormalized()
{
    string path = WriteTempConfig("{ \"Version\": 1, \"Hotkeys\": null, \"Profiles\": null, \"ExcludedProcesses\": null }");
    try
    {
        var result = InputFlowConfig.LoadDetailed(path);

        AssertTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
        AssertEqual(0, result.Config.Hotkeys.Count, "Hotkeys should be normalized.");
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
    var config = CreateKnownWorkingConfig();
    config.Hotkeys[0].Mode = "cycle";

    var errors = InputFlowConfigValidator.Validate(config);

    AssertContains(errors, "Mode 'cycle' is not supported");
}

static InputFlowConfig CreateKnownWorkingConfig()
{
    return new InputFlowConfig
    {
        Version = 1,
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
        Hotkeys = new List<HotkeyConfig>
        {
            new HotkeyConfig
            {
                Name = "Korean toggle",
                Keys = "RightAlt",
                Mode = "toggle",
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
