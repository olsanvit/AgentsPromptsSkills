namespace AgentsPromptsSkills.Mobile.Services;

/// <summary>Wrapper around MAUI SecureStorage for token/credential persistence.</summary>
public class SecureStorageService
{
    public async Task SetAsync(string key, string value)
        => await SecureStorage.Default.SetAsync(key, value);

    public async Task<string?> GetAsync(string key)
        => await SecureStorage.Default.GetAsync(key);

    public bool Remove(string key)
        => SecureStorage.Default.Remove(key);

    public void RemoveAll()
        => SecureStorage.Default.RemoveAll();
}
