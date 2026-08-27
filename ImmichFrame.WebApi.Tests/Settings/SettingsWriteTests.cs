using AwesomeAssertions;
using ImmichFrame.WebApi.Helpers.Config;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Models.Admin;
using ImmichFrame.WebApi.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Settings;

[TestFixture]
public class SettingsWriteTests
{
    private string _configDir = null!;
    private ConfigLocation _location = null!;
    private SettingsFileWriter _writer = null!;
    private ConfigLoader _loader = null!;

    [SetUp]
    public void Setup()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "immichframe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);

        _location = new ConfigLocation(_configDir);
        _writer = new SettingsFileWriter(_location, NullLogger<SettingsFileWriter>.Instance);

        var loggerFactory = LoggerFactory.Create(_ => { });
        _loader = new ConfigLoader(loggerFactory.CreateLogger<ConfigLoader>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true);
    }

    private static ServerSettings SettingsWith(params ServerAccountSettings[] accounts) => new()
    {
        GeneralSettingsImpl = new GeneralSettings(),
        AccountsImpl = accounts.ToList()
    };

    [Test]
    public void WrittenSettings_RoundTripThroughConfigLoader()
    {
        var settings = SettingsWith(new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKey = "literal-key"
        });
        settings.GeneralSettingsImpl!.Interval = 33;
        settings.GeneralSettingsImpl.Layout = "single";

        _writer.Write(settings);

        // The reader uses default (case-sensitive, PascalCase) options, so a camelCase writer would
        // silently produce a file that fails to bind and falls through to the V1/env path.
        var reloaded = _loader.LoadConfig(_configDir);

        reloaded.GeneralSettings.Interval.Should().Be(33);
        reloaded.GeneralSettings.Layout.Should().Be("single");
        reloaded.Accounts.Should().ContainSingle();
        reloaded.Accounts.Single().ImmichServerUrl.Should().Be("http://immich.example.com:2283");
        reloaded.Accounts.Single().ApiKey.Should().Be("literal-key");
    }

    [Test]
    public void FileBackedApiKey_IsNeverWrittenInPlaintext()
    {
        var keyFile = Path.Combine(_configDir, "immich.key");
        File.WriteAllText(keyFile, "super-secret-from-file");

        var account = new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKeyFile = keyFile
        };

        // Resolves ApiKey from the file, exactly as startup does.
        account.ValidateAndInitialize();
        account.ApiKey.Should().Be("super-secret-from-file");

        _writer.Write(SettingsWith(account));

        var raw = File.ReadAllText(_location.SettingsJsonPath);
        raw.Should().NotContain("super-secret-from-file",
            "the resolved key must stay in its file rather than being copied into Settings.json");
        raw.Should().Contain("ApiKeyFile");

        // And the result must still be loadable — writing both fields would throw "Cannot specify both".
        var reloaded = _loader.LoadConfig(_configDir);
        reloaded.Accounts.Single().ApiKey.Should().Be("super-secret-from-file");
    }

    [Test]
    public void ValidateAndInitialize_IsIdempotent()
    {
        var keyFile = Path.Combine(_configDir, "immich.key");
        File.WriteAllText(keyFile, "key-from-file");

        var account = new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKeyFile = keyFile
        };

        account.ValidateAndInitialize();

        // Settings.Validate() cascades into this, so a live reload would otherwise self-destruct
        // once both ApiKey and ApiKeyFile are populated.
        var secondCall = () => account.ValidateAndInitialize();
        secondCall.Should().NotThrow();
    }

    [Test]
    public void Save_PreservesAuthenticationSecret()
    {
        var current = SettingsWith(new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKey = "literal-key"
        });
        current.GeneralSettingsImpl!.AuthenticationSecret = "keep-me-secret";

        // The DTO has no AuthenticationSecret field at all, so the mapper must carry it forward.
        var update = new AdminSettingsUpdateDto
        {
            Version = 1,
            General = new AdminGeneralSettingsDto { Interval = 10 },
            Accounts =
            [
                new AdminAccountUpdateDto { Index = 0, ImmichServerUrl = "http://immich.example.com:2283" }
            ]
        };

        var candidate = AdminSettingsMapper.Apply(update, current);

        candidate.GeneralSettingsImpl!.AuthenticationSecret.Should().Be("keep-me-secret");
        candidate.GeneralSettingsImpl.Interval.Should().Be(10);
    }

    [Test]
    public void Save_WithoutApiKey_KeepsTheExistingOne()
    {
        var current = SettingsWith(new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKey = "existing-key"
        });

        var update = new AdminSettingsUpdateDto
        {
            Version = 1,
            General = new AdminGeneralSettingsDto(),
            Accounts =
            [
                // ApiKey omitted entirely -> keep whatever is configured.
                new AdminAccountUpdateDto { Index = 0, ImmichServerUrl = "http://immich.example.com:2283" }
            ]
        };

        var candidate = AdminSettingsMapper.Apply(update, current);

        candidate.AccountsImpl.Single().ApiKey.Should().Be("existing-key");
    }

    [Test]
    public void Save_WithNewApiKey_ReplacesItAndDropsTheKeyFile()
    {
        var current = SettingsWith(new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKeyFile = "/somewhere/immich.key",
            ApiKey = "resolved-from-file"
        });

        var update = new AdminSettingsUpdateDto
        {
            Version = 1,
            General = new AdminGeneralSettingsDto(),
            Accounts =
            [
                new AdminAccountUpdateDto
                {
                    Index = 0,
                    ImmichServerUrl = "http://immich.example.com:2283",
                    ApiKey = "brand-new-key"
                }
            ]
        };

        var candidate = AdminSettingsMapper.Apply(update, current);
        var account = candidate.AccountsImpl.Single();

        account.ApiKey.Should().Be("brand-new-key");
        account.ApiKeyFile.Should().BeNull("keeping both would fail validation on the next load");
    }

    [Test]
    public void MinimalGeneralBlock_ProducesDefaultsNotZeros()
    {
        var current = SettingsWith(new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKey = "literal-key"
        });

        // A DTO the client left entirely untouched must not zero out working settings.
        var update = new AdminSettingsUpdateDto
        {
            Version = 1,
            General = new AdminGeneralSettingsDto(),
            Accounts =
            [
                new AdminAccountUpdateDto { Index = 0, ImmichServerUrl = "http://immich.example.com:2283" }
            ]
        };

        var candidate = AdminSettingsMapper.Apply(update, current);
        var general = candidate.GeneralSettingsImpl!;
        var defaults = new GeneralSettings();

        general.Interval.Should().Be(defaults.Interval);
        general.Layout.Should().Be(defaults.Layout);
        general.Style.Should().Be(defaults.Style);
        general.Language.Should().Be(defaults.Language);
        general.RenewImagesDuration.Should().Be(defaults.RenewImagesDuration);
        general.RefreshAlbumPeopleInterval.Should().Be(defaults.RefreshAlbumPeopleInterval);
    }

    [Test]
    public void Write_KeepsPreviousFileAsBackup()
    {
        var settings = SettingsWith(new ServerAccountSettings
        {
            ImmichServerUrl = "http://immich.example.com:2283",
            ApiKey = "literal-key"
        });

        settings.GeneralSettingsImpl!.Interval = 11;
        _writer.Write(settings);

        settings.GeneralSettingsImpl.Interval = 22;
        _writer.Write(settings);

        File.Exists(_location.SettingsJsonPath + ".bak").Should().BeTrue();
        File.ReadAllText(_location.SettingsJsonPath).Should().Contain("\"Interval\": 22");
        File.ReadAllText(_location.SettingsJsonPath + ".bak").Should().Contain("\"Interval\": 11");
    }

    [Test]
    public void Validator_RejectsAccountWithoutUrlOrKey()
    {
        var settings = SettingsWith(new ServerAccountSettings());

        SettingsValidator.TryValidate(settings, out var problems).Should().BeFalse();
        problems.Should().Contain(p => p.Contains("ImmichServerUrl is required"));
        problems.Should().Contain(p => p.Contains("ApiKey or ApiKeyFile"));
    }

    [Test]
    public void Validator_RejectsEmptyAccountList()
    {
        SettingsValidator.TryValidate(SettingsWith(), out var problems).Should().BeFalse();
        problems.Should().Contain(p => p.Contains("At least one Immich account"));
    }
}
