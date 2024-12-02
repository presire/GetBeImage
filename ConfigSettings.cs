using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


namespace GetBeImage
{
    public class ConfigSettings
    {
        public string? Be { get; set; } = "";
        
        public List<string> Skip { get; set; } = [];

        public bool Maguro { get; set; } = true;
        
        public string? Dir { get; set; } = "";
        
        public bool Maximize { get; set; }
        
        public List<double> WindowSize { get; set; } = [1024.0, 768.0];
    }

    public sealed class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance = 
            new Lazy<ConfigManager>(() => new ConfigManager());
        
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private ConfigSettings _currentConfig = null!;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static ConfigManager Instance => _instance.Value;

        private ConfigManager()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GetBeImage.json");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<ConfigSettings> GetConfigAsync()
        {
            if (_currentConfig != null)
                return _currentConfig;

            await _semaphore.WaitAsync();
            try
            {
                if (_currentConfig == null)
                {
                    await InitializeConfigFileAsync();
                    var jsonString = await File.ReadAllTextAsync(_filePath);
                    _currentConfig = JsonSerializer.Deserialize<ConfigSettings>(jsonString, _jsonOptions) 
                        ?? new ConfigSettings();
                }
                return _currentConfig;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task SaveConfigInternalAsync(ConfigSettings config)
        {
            var jsonString = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, jsonString);
            _currentConfig = config;
        }

        public async Task UpdateConfigAsync(Action<ConfigSettings> updateAction)
        {
            await _semaphore.WaitAsync();
            try
            {
                var config = await GetConfigAsync();
                updateAction(config);
                await SaveConfigInternalAsync(config);
            }
            catch (Exception ex)
            {
                throw new Exception($"設定ファイルの更新に失敗しました。 : {ex.Message}", ex);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task InitializeConfigFileAsync()
        {
            if (!File.Exists(_filePath))
            {
                var defaultConfig = new ConfigSettings();
                var directoryPath = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await SaveConfigInternalAsync(defaultConfig);
            }
        }
    }
}
