using System.ComponentModel;
using System.Runtime.CompilerServices;
using BiBiVoiceWin;

namespace BiBiVoiceWin.Settings;

/// <summary>
/// 设置界面用的 API 配置视图模型，负责编辑与自动保存触发。
/// </summary>
public sealed class ApiProfileItem : INotifyPropertyChanged
{
    private string _id;
    private string _name;
    private string _endpoint;
    private string _apiKey;
    private string _model;
    private double _temperature;
    private string _reasoningEffort;
    private bool _logIncludeSecrets;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ApiProfileItem(ApiProfile profile)
    {
        _id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id;
        _name = string.IsNullOrWhiteSpace(profile.Name) ? "未命名" : profile.Name;
        _endpoint = profile.Endpoint ?? "";
        _apiKey = profile.ApiKey ?? "";
        _model = profile.Model ?? "";
        _temperature = profile.Temperature;
        _reasoningEffort = profile.ReasoningEffort ?? "low";
        _logIncludeSecrets = profile.LogIncludeSecrets;
    }

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Endpoint
    {
        get => _endpoint;
        set => SetField(ref _endpoint, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetField(ref _apiKey, value);
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public double Temperature
    {
        get => _temperature;
        set => SetField(ref _temperature, value);
    }

    public string ReasoningEffort
    {
        get => _reasoningEffort;
        set => SetField(ref _reasoningEffort, value);
    }

    public bool LogIncludeSecrets
    {
        get => _logIncludeSecrets;
        set => SetField(ref _logIncludeSecrets, value);
    }

    public ApiProfile ToConfig()
    {
        var id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        var name = string.IsNullOrWhiteSpace(Name) ? "未命名" : Name.Trim();

        return new ApiProfile
        {
            Id = id,
            Name = name,
            Endpoint = Endpoint?.Trim() ?? "",
            ApiKey = ApiKey?.Trim() ?? "",
            Model = Model?.Trim() ?? "",
            Temperature = (float)Temperature,
            ReasoningEffort = ReasoningEffort?.Trim() ?? "low",
            LogIncludeSecrets = LogIncludeSecrets
        };
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
