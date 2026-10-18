using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Moru;

internal sealed record CharacterBackupManifest
{
    public string Format { get; init; } = "Moru.Characters";
    public int Version { get; init; } = 1;
    public string SelectedSkinId { get; init; } = "sprout";
    public SkinDefinition[] Skins { get; init; } = [];
    public Dictionary<string, string> BuiltInNames { get; init; } = new();
    public Dictionary<string, string> ImageHashes { get; init; } = new();
}

// A backup contains only character data, never account settings, logs or original file paths.
internal sealed class CharacterBackup : IDisposable
{
    internal const long MaxArchiveBytes = 64 * 1024 * 1024;
    internal const long MaxExpandedBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 16 };
    private readonly string staging;
    internal CharacterBackupManifest Manifest { get; }
    internal IReadOnlyList<Skin> Skins { get; }
    internal int ImageCount => Files(Manifest.Skins).Count();
    private CharacterBackup(string directory, CharacterBackupManifest manifest, IReadOnlyList<Skin> skins)
    { staging = directory; Manifest = manifest; Skins = skins; }

    internal static void Export(string destination, string userDirectory, IReadOnlyList<Skin> all, Preferences preferences)
    {
        var custom = all.Where(s => s.IsCustom).ToArray();
        var definitions = new List<SkinDefinition>();
        var sources = new Dictionary<string, string>();
        for (int i = 0; i < custom.Length; i++)
        {
            var skin = custom[i];
            if (!ValidCustom(skin.Definition)) throw Invalid("백업할 캐릭터 정보를 읽지 못했어요.");
            string image = $"skin-{i}.png";
            sources.Add(image, Path.Combine(userDirectory, skin.Definition.ImageFile!));
            var states = new Dictionary<string, string>();
            foreach (var pair in skin.Definition.StateImages ?? new())
            {
                string file = $"skin-{i}-{pair.Key}.png";
                sources.Add(file, Path.Combine(userDirectory, pair.Value));
                states.Add(pair.Key, file);
            }
            definitions.Add(skin.Definition with { Name = DisplayName(skin, preferences), ImageFile = image,
                StateImages = states.Count > 0 ? states : null });
        }
        var manifest = new CharacterBackupManifest { SelectedSkinId = preferences.SkinId, Skins = definitions.ToArray(),
            BuiltInNames = all.Where(s => !s.IsCustom).ToDictionary(s => s.Id, s => DisplayName(s, preferences)) };
        string path = Path.GetFullPath(destination);
        string userRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDirectory)) + Path.DirectorySeparatorChar;
        if (path.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(userDirectory))!, "settings.json"), StringComparison.OrdinalIgnoreCase))
            throw Invalid("백업은 바탕화면이나 문서 등 캐릭터 저장 폴더 밖에 저장해 주세요.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                long total = 0;
                foreach (var pair in sources)
                {
                    using var input = new FileStream(pair.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
                    total += input.Length;
                    if (input.Length > PngSkinImage.MaxFileBytes || total > MaxExpandedBytes) throw Invalid("이미지 용량이 백업 한도를 넘었어요.");
                    using var output = archive.CreateEntry(pair.Key, CompressionLevel.Fastest).Open();
                    manifest.ImageHashes[pair.Key] = CopyBounded(input, output, input.Length, PngSkinImage.MaxFileBytes);
                }
                using (var json = archive.CreateEntry("backup.json").Open()) JsonSerializer.Serialize(json, manifest, JsonOptions);
            }
            // Validate the complete ZIP and every image before replacing a previous backup.
            using (Read(temporary)) { }
            File.Move(temporary, path, overwrite: true);
        }
        finally { TryDeleteFile(temporary); }
    }

    internal static CharacterBackup Read(string path)
    {
        string stage = Path.Combine(Path.GetTempPath(), "Moru-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 22 or > MaxArchiveBytes) throw Invalid("백업 파일 크기를 확인해 주세요. 최대 64 MiB까지 열 수 있어요.");
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count is < 1 or > 73) throw Invalid("백업에 파일이 너무 많아요.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName != "backup.json" && !Regex.IsMatch(entry.FullName, @"\Askin-[0-9]{1,2}(?:-(?:Idle|Working|Done|Waiting|Error))?\.png\z"))
                    throw Invalid("모루 캐릭터 백업 형식이 아니에요.");
                long limit = entry.FullName == "backup.json" ? 65536 : PngSkinImage.MaxFileBytes;
                if (!entries.TryAdd(entry.FullName, entry) || entry.Length > limit || entry.Length < 1)
                    throw Invalid("백업에 중복되거나 너무 큰 파일이 있어요.");
                total += entry.Length;
                if (total > MaxExpandedBytes) throw Invalid("백업의 전체 이미지 용량이 너무 커요.");
            }
            if (!entries.TryGetValue("backup.json", out var metadata)) throw Invalid("캐릭터 백업 정보가 없어요.");
            using var bytes = new MemoryStream();
            using (var source = metadata.Open()) CopyBounded(source, bytes, metadata.Length, 65536);
            var manifest = JsonSerializer.Deserialize<CharacterBackupManifest>(bytes.ToArray(), JsonOptions);
            ValidateManifest(manifest);
            var referenced = Files(manifest!.Skins).Append("backup.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!referenced.SetEquals(entries.Keys)) throw Invalid("백업에 필요한 이미지가 없거나 알 수 없는 파일이 있어요.");
            Directory.CreateDirectory(stage);
            foreach (string name in referenced.Where(n => n != "backup.json"))
            {
                using var source = entries[name].Open();
                using var target = new FileStream(Path.Combine(stage, name), FileMode.CreateNew, FileAccess.Write);
                string hash = CopyBounded(source, target, entries[name].Length, PngSkinImage.MaxFileBytes);
                if (!string.Equals(hash, manifest.ImageHashes[name], StringComparison.OrdinalIgnoreCase))
                    throw Invalid("백업 이미지가 손상되었어요. 다른 백업 파일을 선택해 주세요.");
            }
            var skins = manifest.Skins.Select(d => new Skin(d, PngSkinImage.Load(Path.Combine(stage, d.ImageFile!)), true,
                (d.StateImages ?? new()).ToDictionary(p => p.Key, p => PngSkinImage.Load(Path.Combine(stage, p.Value))))).ToArray();
            return new CharacterBackup(stage, manifest, skins);
        }
        catch (JsonException ex) { DeleteStage(stage); throw new InvalidDataException("백업 정보를 읽지 못했어요. 모루에서 저장한 백업을 선택해 주세요.", ex); }
        catch { DeleteStage(stage); throw; }
    }

    internal (IReadOnlyList<Skin> All, Preferences Preferences, int Added, int Existing) Restore(
        string userDirectory, IReadOnlyList<Skin> all, Preferences preferences, Func<Preferences, bool> save)
    {
        var candidates = all.Where(s => s.IsCustom).ToArray();
        var fingerprints = new Dictionary<string, string>();
        foreach (var skin in candidates)
        {
            // An unreadable current skin is kept; it can never be mistaken for a duplicate.
            try { fingerprints[skin.Id] = Fingerprint(skin, userDirectory, DisplayName(skin, preferences)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        }
        var claimed = new HashSet<string>();
        var mapping = new Dictionary<string, string>();
        var additions = new List<(Skin Source, string Id)>();
        foreach (var skin in Skins)
        {
            string fingerprint = Fingerprint(skin, staging, skin.Name);
            var duplicate = candidates.Where(s => !claimed.Contains(s.Id) && fingerprints.GetValueOrDefault(s.Id) == fingerprint)
                .OrderByDescending(s => s.Id == skin.Id).FirstOrDefault();
            string id;
            if (duplicate is not null) id = duplicate.Id;
            else
            {
                id = skin.Id;
                if (all.Any(s => s.Id == id) || File.Exists(Path.Combine(userDirectory, id + ".json"))
                    || Directory.Exists(userDirectory) && Directory.EnumerateFiles(userDirectory, id + "*").Any())
                    id = "user-" + Guid.NewGuid().ToString("N");
                additions.Add((skin, id));
            }
            claimed.Add(id); mapping.Add(skin.Id, id);
        }
        int onDisk = Directory.Exists(userDirectory) ? Directory.EnumerateFiles(userDirectory, "*.json").Count() : 0;
        if (all.Count + additions.Count > 12 || all.Count(s => !s.IsCustom) + onDisk + additions.Count > 12)
            throw Invalid($"친구는 기본 캐릭터를 포함해 최대 12명이에요. 이번 백업은 {additions.Count}명을 추가해야 해요. 먼저 목록을 정리해 주세요.");

        string stage = Path.Combine(userDirectory, ".restore-" + Guid.NewGuid().ToString("N"));
        var committed = new List<string>();
        var restored = new List<Skin>();
        try
        {
            if (additions.Count > 0) Directory.CreateDirectory(stage);
            foreach (var (source, id) in additions)
            {
                string image = id + ".png";
                File.Copy(Path.Combine(staging, source.Definition.ImageFile!), Path.Combine(stage, image));
                var states = new Dictionary<string, string>();
                foreach (var pair in source.Definition.StateImages ?? new())
                {
                    string file = id + "-" + pair.Key + "-" + Guid.NewGuid().ToString("N") + ".png";
                    File.Copy(Path.Combine(staging, pair.Value), Path.Combine(stage, file));
                    states.Add(pair.Key, file);
                }
                var definition = source.Definition with { Id = id, ImageFile = image, StateImages = states.Count > 0 ? states : null };
                File.WriteAllText(Path.Combine(stage, id + ".json"), JsonSerializer.Serialize(definition, JsonOptions));
                restored.Add(new Skin(definition, source.Image, true, source.StateImages));
            }
            var combined = all.Concat(restored).ToArray();
            var names = preferences.SkinNames.Where(p => all.Any(s => s.Id == p.Key)).ToDictionary(p => p.Key, p => p.Value);
            foreach (var pair in Manifest.BuiltInNames.Where(p => all.Any(s => !s.IsCustom && s.Id == p.Key))) names[pair.Key] = pair.Value;
            string selected = mapping.GetValueOrDefault(Manifest.SelectedSkinId, Manifest.SelectedSkinId);
            if (!combined.Any(s => s.Id == selected)) selected = preferences.SkinId;
            var updated = preferences with { SkinId = selected, SkinNames = names,
                HiddenSkinIds = preferences.HiddenSkinIds.Where(id => id != selected).ToArray() };
            if (additions.Count > 0)
                foreach (var file in Directory.EnumerateFiles(stage).OrderBy(p => Path.GetExtension(p) == ".json"))
                {
                    string destination = Path.Combine(userDirectory, Path.GetFileName(file));
                    File.Move(file, destination); // No overwrites, including an unexpected on-disk collision.
                    committed.Add(destination);
                }
            if (!save(updated)) throw new IOException("복원한 캐릭터 설정을 저장하지 못했어요. 기존 친구는 그대로 유지했어요.");
            return (combined, updated, restored.Count, Skins.Count - restored.Count);
        }
        catch { foreach (string path in committed) TryDeleteFile(path); throw; }
        finally { DeleteStage(stage); }
    }

    private static string DisplayName(Skin skin, Preferences preferences) => preferences.SkinNames.TryGetValue(skin.Id, out var name)
        && SkinNamesPolicy.IsValid(name) ? name.Trim() : skin.Name;
    private static bool ValidCustom(SkinDefinition? d) => SkinCatalog.IsValid(d) && d!.Shape == "image"
        && Regex.IsMatch(d.Id, @"\Auser-[a-f0-9]{32}\z") && SkinNamesPolicy.IsValid(d.Name);
    private static void ValidateManifest(CharacterBackupManifest? m)
    {
        if (m is null || m.Format != "Moru.Characters") throw Invalid("모루 캐릭터 백업 파일을 선택해 주세요.");
        if (m.Version != 1) throw Invalid("이 백업 버전을 지원하지 않아요. 최신 모루에서 다시 시도해 주세요.");
        if (m.Skins is null || m.Skins.Length > 12 || m.Skins.Any(d => !ValidCustom(d))
            || m.Skins.Select(d => d.Id).Distinct().Count() != m.Skins.Length || m.BuiltInNames is null || m.BuiltInNames.Count > 12
            || m.BuiltInNames.Any(p => !Regex.IsMatch(p.Key, @"\A[a-z][a-z0-9-]{0,39}\z") || p.Key.StartsWith("user-") || !SkinNamesPolicy.IsValid(p.Value))
            || m.SelectedSkinId is null || !(m.Skins.Any(d => d.Id == m.SelectedSkinId) || m.BuiltInNames.ContainsKey(m.SelectedSkinId)))
            throw Invalid("백업의 캐릭터 정보가 올바르지 않아요.");
        if (m.ImageHashes is null || !Files(m.Skins).ToHashSet().SetEquals(m.ImageHashes.Keys)
            || m.ImageHashes.Values.Any(hash => hash is null || !Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z")))
            throw Invalid("백업의 이미지 검사 정보가 올바르지 않아요.");
    }
    private static IEnumerable<string> Files(IEnumerable<SkinDefinition> skins) => skins.SelectMany(d =>
        new[] { d.ImageFile! }.Concat(d.StateImages?.Values ?? Enumerable.Empty<string>())).Distinct(StringComparer.OrdinalIgnoreCase);
    private static string Fingerprint(Skin skin, string directory, string name)
    {
        var d = skin.Definition;
        string Hash(string file)
        {
            using var stream = File.OpenRead(Path.Combine(directory, file));
            if (stream.Length > PngSkinImage.MaxFileBytes) throw Invalid("현재 캐릭터의 이미지 용량이 너무 커요.");
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        return JsonSerializer.Serialize(new { name, d.Description, d.Body, d.Outline, d.Accent, d.Soft, d.Cheek,
            Image = Hash(d.ImageFile!), States = (d.StateImages ?? new()).OrderBy(p => p.Key).Select(p => new { State = p.Key, Hash = Hash(p.Value) }).ToArray() });
    }
    private static string CopyBounded(Stream source, Stream target, long expected, long maximum)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long copied = 0; int read;
        while ((read = source.Read(buffer)) > 0)
        { copied += read; if (copied > expected || copied > maximum) throw Invalid("백업 파일의 크기 정보가 올바르지 않아요."); hash.AppendData(buffer, 0, read); target.Write(buffer, 0, read); }
        if (copied != expected) throw Invalid("백업 파일이 중간에 손상되었어요.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static InvalidDataException Invalid(string message) => new(message);
    private static void TryDeleteFile(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void DeleteStage(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void Dispose() => DeleteStage(staging);
}
