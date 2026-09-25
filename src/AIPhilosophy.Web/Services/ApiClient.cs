using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace AIPhilosophy.Web.Services;

public class ApiClient
{
    public const string TokenKey = "philosophy365_token";
    public const string UserKey = "philosophy365_user";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    public ApiClient(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public string? Jwt { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }
    public Guid? TenantId { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
    public bool IsAuthenticated => !string.IsNullOrEmpty(Jwt);
    public bool IsAdmin => Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        var token = await _js.InvokeAsync<string>("localStorage.getItem", TokenKey);
        var user = await _js.InvokeAsync<string>("localStorage.getItem", UserKey);
        if (!string.IsNullOrEmpty(token)) Jwt = token;
        if (!string.IsNullOrEmpty(user))
        {
            try
            {
                using var doc = JsonDocument.Parse(user);
                var root = doc.RootElement;
                DisplayName = root.TryGetProperty("displayName", out var d) ? d.GetString() : null;
                Email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
                if (root.TryGetProperty("tenantId", out var tid) && tid.ValueKind == JsonValueKind.String)
                    TenantId = Guid.TryParse(tid.GetString(), out var g) ? g : null;
                Roles = root.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array
                    ? r.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList()
                    : Array.Empty<string>();
            }
            catch { }
        }
    }

    public async Task<bool> RefreshProfileAsync()
    {
        if (!IsAuthenticated) return false;
        var me = await GetAsync<ApiEnvelope<MePayload>>("api/auth/me");
        if (me is not { Success: true } || me.Data == null) return false;

        DisplayName = me.Data.displayName ?? DisplayName;
        Email = me.Data.email ?? Email;
        if (me.Data.tenantId != null) TenantId = me.Data.tenantId;
        Roles = me.Data.roles ?? new List<string>();

        var payload = JsonSerializer.Serialize(new { email = Email, displayName = DisplayName, tenantId = TenantId, roles = Roles });
        await _js.InvokeVoidAsync("localStorage.setItem", UserKey, payload);
        return true;
    }

    public async Task SaveSessionAsync(string token, string email, string displayName, Guid tenantId)
    {
        Jwt = token;
        Email = email;
        DisplayName = displayName;
        TenantId = tenantId;
        await _js.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        var payload = JsonSerializer.Serialize(new { email, displayName, tenantId });
        await _js.InvokeVoidAsync("localStorage.setItem", UserKey, payload);
    }

    public async Task ClearSessionAsync()
    {
        Jwt = null;
        Email = null;
        DisplayName = null;
        TenantId = null;
        Roles = Array.Empty<string>();
        await _js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        await _js.InvokeVoidAsync("localStorage.removeItem", UserKey);
    }

    private void AttachAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(Jwt))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt);
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        AttachAuth(req);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return default;
        try
        {
            return await resp.Content.ReadFromJsonAsync<T>();
        }
        catch
        {
            return default;
        }
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        AttachAuth(req);
        if (body != null) req.Content = JsonContent.Create(body);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return default;
        }
    }

    public async Task<ApiEnvelope<T>?> PostEnvelopeAsync<T>(string path, object? body = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        AttachAuth(req);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType(), mediaType: null, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<ApiEnvelope<T>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ApiEnvelope<T>?> PutEnvelopeAsync<T>(string path, object? body = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, path);
        AttachAuth(req);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType(), mediaType: null, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<ApiEnvelope<T>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ApiEnvelope<T>?> DeleteEnvelopeAsync<T>(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, path);
        AttachAuth(req);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<ApiEnvelope<T>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

public class ApiEnvelope<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

public record MePayload(
    string? displayName,
    string? email,
    List<string>? roles,
    Guid? tenantId);