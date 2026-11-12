// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// 阵容分享/导入：通过 dpaste.com 匿名 API 把阵容 JSON 上传成短链接。
/// 文档：https://dpaste.com/api/v2/
/// </summary>
public class LineupShareService
{
    private const string DpasteEndpoint = "https://dpaste.com/api/v2/";
    private const int ExpiryDays = 365; // dpaste 上限

    private static readonly Regex DpasteUrlRegex =
        new(@"https?://dpaste\.com/[A-Za-z0-9]+", RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly LineupEquipmentService _equipmentRules;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _exportGate = new(1, 1);

    public LineupShareService(LineupEquipmentService equipmentRules, string cacheDirectory, HttpClient? httpClient = null)
    {
        _equipmentRules = equipmentRules;
        _cacheDirectory = cacheDirectory;
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ASPAssistant-LineupShare/1.0");
        }
    }

    /// <summary>返回阵容短链接；相同内容在首次生成后的 30 天内复用本地记录。</summary>
    public async Task<string> ExportAsync(Lineup lineup, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(lineup, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            lineup.Name, lineup.Slots, lineup.EarlySlots, lineup.MidSlots, lineup.SupportSlots, lineup.Strategies, lineup.TargetCovenants
        }, JsonOptions)));

        await _exportGate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = Path.Combine(_cacheDirectory, fingerprint + ".json");
            CachedLink? cached;
            try
            {
                cached = JsonSerializer.Deserialize<CachedLink>(await File.ReadAllTextAsync(path, ct), JsonOptions)
                    ?? throw new InvalidDataException("分享链接缓存为空");
            }
            catch (FileNotFoundException)
            {
                cached = null;
            }
            if (cached is not null)
            {
                if (string.IsNullOrEmpty(cached.Url) || DpasteUrlRegex.Match(cached.Url).Value != cached.Url
                    || cached.CreatedAt == default)
                    throw new InvalidDataException("分享链接缓存内容无效");
                if (DateTimeOffset.UtcNow - cached.CreatedAt < TimeSpan.FromDays(30))
                    return cached.Url;
            }

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("content", json),
                new KeyValuePair<string, string>("syntax", "json"),
                new KeyValuePair<string, string>("expiry_days", ExpiryDays.ToString()),
            });

            using var response = await _http.PostAsync(DpasteEndpoint, content, ct);
            response.EnsureSuccessStatusCode();

            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
            var match = DpasteUrlRegex.Match(body);
            if (!match.Success)
                throw new InvalidOperationException($"dpaste 响应不含可识别的 URL：{body}");
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new CachedLink
                {
                    Url = match.Value, CreatedAt = DateTimeOffset.UtcNow
                }, JsonOptions), ct);
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
            return match.Value;
        }
        finally { _exportGate.Release(); }
    }

    private sealed class CachedLink
    {
        public required string Url { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }

    /// <summary>
    /// 从 dpaste 短链接拉取并反序列化阵容；URL 不合法或非 dpaste 链接时抛出。
    /// 也支持直接传入 JSON 字符串，方便用户手动粘贴。
    /// </summary>
    public async Task<Lineup> ImportAsync(string urlOrJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrJson))
            throw new ArgumentException("导入内容为空", nameof(urlOrJson));

        urlOrJson = urlOrJson.Trim();

        string json;
        if (urlOrJson.StartsWith("{"))
        {
            json = urlOrJson;
        }
        else
        {
            var match = DpasteUrlRegex.Match(Regex.Replace(urlOrJson, @"^<[^\r\n]*>\s*", ""));
            if (!match.Success)
                throw new ArgumentException("导入内容必须是 dpaste.com 短链接或阵容 JSON 字符串");
            // 取 raw 文本视图
            var rawUrl = match.Value.TrimEnd('/') + ".txt";
            json = await _http.GetStringAsync(rawUrl, ct);
        }

        var lineup = JsonSerializer.Deserialize<Lineup>(json, JsonOptions)
                     ?? throw new InvalidOperationException("无法解析阵容 JSON");
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        _equipmentRules.Upgrade(lineup);
        return lineup;
    }
}
