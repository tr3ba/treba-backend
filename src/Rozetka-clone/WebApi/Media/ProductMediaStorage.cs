using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace WebApi.Media;

public sealed class ProductMediaStorage(IOptions<ProductMediaOptions> options, IWebHostEnvironment environment) : IProductMediaStorage, IDisposable
{
    private readonly ProductMediaOptions _options = options.Value;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 });
    private readonly SemaphoreSlim _transfers = new(4);
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private static readonly Regex FilePattern = new("^[a-f0-9]{32}\\.(png|jpe?g|webp|gif)\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task SaveAsync(Guid productId, string fileName, byte[] bytes, CancellationToken ct)
    {
        Validate(productId, fileName);
        if (bytes.Length is <= 0 or > MaxImageBytes) throw new ArgumentException("Invalid photo size.");
        if (_options.Provider == "Local")
        {
            var path = LocalPath(productId, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        else
        {
            var temporary = Path.Combine(Path.GetTempPath(), $"treba-media-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, ct);
                await TransferAsync($"-mkdir {Quote(RemoteDirectory(productId))}\nput {Quote(temporary)} {Quote(RemotePath(productId, fileName))}\n", ct);
            }
            finally { File.Delete(temporary); }
        }
        _cache.Set(CacheKey(productId, fileName), new ProductMediaFile(bytes, ContentType(fileName)),
            new MemoryCacheEntryOptions().SetSize(bytes.Length).SetAbsoluteExpiration(TimeSpan.FromMinutes(30)));
    }

    public async Task<ProductMediaFile?> ReadAsync(Guid productId, string fileName, CancellationToken ct)
    {
        Validate(productId, fileName);
        var key = CacheKey(productId, fileName);
        if (_cache.TryGetValue<ProductMediaFile>(key, out var cached)) return cached;
        byte[] bytes;
        if (_options.Provider == "Local")
        {
            var path = LocalPath(productId, fileName);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxImageBytes) throw new IOException("Photo exceeds the maximum size.");
            bytes = await File.ReadAllBytesAsync(path, ct);
        }
        else
        {
            var temporary = Path.Combine(Path.GetTempPath(), $"treba-media-{Guid.NewGuid():N}.tmp");
            try
            {
                if (!await TransferAsync($"get {Quote(RemotePath(productId, fileName))} {Quote(temporary)}\n", ct, allowMissing: true)) return null;
                if (new FileInfo(temporary).Length > MaxImageBytes) throw new IOException("Photo exceeds the maximum size.");
                bytes = await File.ReadAllBytesAsync(temporary, ct);
            }
            finally { File.Delete(temporary); }
        }
        var media = new ProductMediaFile(bytes, ContentType(fileName));
        _cache.Set(key, media, new MemoryCacheEntryOptions().SetSize(bytes.Length).SetAbsoluteExpiration(TimeSpan.FromMinutes(30)));
        return media;
    }

    public async Task DeleteAsync(Guid productId, string fileName, CancellationToken ct)
    {
        Validate(productId, fileName);
        if (_options.Provider == "Local") File.Delete(LocalPath(productId, fileName));
        else await TransferAsync($"rm {Quote(RemotePath(productId, fileName))}\n", ct, allowMissing: true);
        _cache.Remove(CacheKey(productId, fileName));
    }

    private string LocalPath(Guid id, string name)
    {
        var root = string.IsNullOrWhiteSpace(_options.RootPath)
            ? Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "products")
            : Path.GetFullPath(_options.RootPath);
        return Path.Combine(root, id.ToString("N"), name);
    }

    private string RemoteDirectory(Guid id) => _options.RootPath.TrimEnd('/') + "/" + id.ToString("N");
    private string RemotePath(Guid id, string name) => RemoteDirectory(id) + "/" + name;
    private static string Quote(string value) => "\"" + value.Replace('\\', '/').Replace("\"", "\\\"").Replace("\n", "").Replace("\r", "") + "\"";
    private static string CacheKey(Guid id, string name) => id.ToString("N") + "/" + name;
    private static void Validate(Guid id, string name)
    {
        if (id == Guid.Empty || !FilePattern.IsMatch(name)) throw new ArgumentException("Invalid product photo path.");
    }
    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", _ => "application/octet-stream"
    };

    private async Task<bool> TransferAsync(string commands, CancellationToken ct, bool allowMissing = false)
    {
        await _transfers.WaitAsync(ct);
        try
        {
            var start = new ProcessStartInfo(_options.SftpExecutable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-q", "-b", "-", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ConnectTimeout=10", "-P", _options.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", _options.SshKeyPath, _options.SshUsername + "@" + _options.SshHost })
                start.ArgumentList.Add(argument);
            if (!string.IsNullOrWhiteSpace(_options.AgentEnvironmentFile))
            {
                var agent = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(_options.AgentEnvironmentFile, ct));
                if (agent is not null)
                    foreach (var name in new[] { "SSH_AUTH_SOCK", "SSH_AGENT_PID" })
                        if (agent.TryGetValue(name, out var value)) start.Environment[name] = value;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var process = Process.Start(start) ?? throw new IOException("Could not start media transfer.");
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.StandardInput.WriteAsync(commands.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                await output;
                var errorText = await error;
                if (process.ExitCode == 0) return true;
                if (allowMissing && errorText.Contains("No such file", StringComparison.OrdinalIgnoreCase)) return false;
                throw new IOException("Shared photo storage is unavailable. Check the SSH key and media folder permissions.");
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
        }
        finally { _transfers.Release(); }
    }

    public void Dispose() { _cache.Dispose(); _transfers.Dispose(); }
}
