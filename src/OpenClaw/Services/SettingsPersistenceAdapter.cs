// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

internal sealed class SettingsPersistenceAdapter
{
    private readonly ConfigurationService _configuration;
    private readonly IAppLogger _logger;

    public SettingsPersistenceAdapter(ConfigurationService configuration, IAppLogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AppSettings Current => _configuration.Settings;

    public void Save()
    {
        _configuration.Save();
        _logger.Info("Settings saved.");
    }

    public EnvironmentConfig? GetSelectedEnvironment()
    {
        return _configuration.GetSelectedEnvironment();
    }
}
