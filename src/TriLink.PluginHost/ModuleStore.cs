using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using TriLink.Plugin;

namespace TriLink.PluginHost
{
    public sealed class ModuleSelection
    {
        public int schemaVersion { get; set; } = 1;
        public string[] enabled { get; set; } = new string[0];
        // Values are generated directory tokens, never caller-supplied paths.
        public Dictionary<string, string> packages { get; set; } = new Dictionary<string, string>();
    }

    // Shared by bootstrap and the management plugin: one interpretation of desired state.
    // Import never loads code and never overwrites an assembly already in use.
    public sealed class ModuleStore
    {
        private const int MaxManifestBytes = 64 * 1024;
        private const int MaxAssemblyBytes = 16 * 1024 * 1024;
        private readonly string _pluginRoot;
        private readonly PluginProfile _profile;
        private readonly string _dataRoot;
        private readonly string _selectionPath;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public ModuleStore(IHostEnvironment environment, string pluginRoot = null, string profilePath = null)
        {
            _pluginRoot = Path.GetFullPath(pluginRoot ?? Path.Combine(environment.BaseDirectory, "plugins"));
            _profile = PluginProfile.Load(profilePath ?? Path.Combine(environment.BaseDirectory,
                "profiles", environment.ProfileName + ".profile.json"));
            _dataRoot = Path.Combine(environment.BaseDirectory, "module-data");
            _selectionPath = Path.Combine(_dataRoot, _profile.name + ".json");
        }

        public ModuleSelection BuiltIns()
        {
            return new ModuleSelection { enabled = _profile.PluginIds.ToArray() };
        }

        public ModuleSelection ReadSelection(bool safeMode)
        {
            if (safeMode || !File.Exists(_selectionPath)) { return BuiltIns(); }
            var value = _json.Deserialize<ModuleSelection>(ReadSmallText(_selectionPath));
            ValidateSelection(value);
            return value;
        }

        public static ModuleSelection Clone(ModuleSelection selection)
        {
            return new ModuleSelection { enabled = selection.enabled.ToArray(),
                packages = new Dictionary<string, string>(selection.packages, StringComparer.OrdinalIgnoreCase) };
        }

        public static bool Equivalent(ModuleSelection left, ModuleSelection right)
        {
            return new HashSet<string>(left.enabled, StringComparer.OrdinalIgnoreCase).SetEquals(right.enabled)
                && left.packages.Count == right.packages.Count
                && left.packages.All(pair => right.packages.ContainsKey(pair.Key)
                    && right.packages[pair.Key] == pair.Value);
        }

        public IReadOnlyList<PluginManifest> Resolve(ModuleSelection selection)
        {
            ValidateSelection(selection);
            var result = new Dictionary<string, PluginManifest>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.GetDirectories(_pluginRoot).OrderBy(p => p, StringComparer.Ordinal))
            {
                var path = Path.Combine(directory, "plugin.json");
                if (!File.Exists(path)) { continue; }
                var manifest = ReadManifest(path);
                if (result.ContainsKey(manifest.id)) { throw new InvalidDataException("Duplicate plugin id: " + manifest.id); }
                result.Add(manifest.id, manifest);
            }
            foreach (var pair in selection.packages)
            {
                var manifest = ReadManifest(Path.Combine(_dataRoot, "packages", pair.Value, "plugin.json"));
                if (manifest.id != pair.Key) { throw new InvalidDataException("Package id does not match selection: " + pair.Key); }
                result[pair.Key] = manifest;
            }
            var enabled = new HashSet<string>(selection.enabled, StringComparer.OrdinalIgnoreCase);
            foreach (var id in enabled)
            {
                if (!result.ContainsKey(id)) { throw new InvalidDataException("Profile selects missing plugin: " + id); }
            }
            foreach (var item in result.Values) { item.enabled = enabled.Contains(item.id); }
            return result.Values.OrderBy(item => item.id, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        public void Validate(ModuleSelection selection)
        {
            var manifests = Resolve(selection);
            // A desktop profile must always retain a route to management and recovery.
            if (_profile.PluginIds.Contains("trilink.desktop"))
            {
                foreach (var id in new[] { "trilink.desktop", "trilink.modules" })
                {
                    if (!selection.enabled.Contains(id)) { throw new InvalidDataException("必须保留桌面与扩展管理模块：" + id); }
                }
            }
            PluginDependencyGraph.Order(manifests);
            var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in manifests.Where(item => item.enabled))
            {
                var name = VerifyAssembly(manifest);
                if (!assemblies.Add(name)) { throw new InvalidDataException("启用模块的程序集名称冲突：" + name); }
            }
        }

        public string DisableBlockedReason(ModuleSelection selection, string id)
        {
            var candidate = Clone(selection);
            candidate.enabled = candidate.enabled.Where(value => value != id).ToArray();
            try
            {
                // Cheap graph-only check for UI selection, no repeated DLL hashing.
                if (id == "trilink.desktop" || id == "trilink.modules") { return "基础入口模块，不能禁用。"; }
                PluginDependencyGraph.Order(Resolve(candidate));
                return null;
            }
            catch (InvalidDataException exception) { return "其他启用模块依赖此模块：" + exception.Message; }
        }

        public void Save(ModuleSelection selection)
        {
            Validate(selection);
            EnsureNoReparse(_dataRoot);
            Directory.CreateDirectory(_dataRoot);
            EnsureNoReparse(_selectionPath);
            var temporary = Path.Combine(_dataRoot, Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, _json.Serialize(selection), new UTF8Encoding(false));
            try
            {
                if (File.Exists(_selectionPath))
                {
                    var previous = _selectionPath + ".previous";
                    EnsureNoReparse(previous);
                    File.Replace(temporary, _selectionPath, previous);
                }
                else { File.Move(temporary, _selectionPath); }
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }

        public ModulePackagePreview Preview(string path)
        {
            var manifest = ReadManifest(path);
            VerifyAssembly(manifest);
            return new ModulePackagePreview { Id = manifest.id, Name = manifest.displayName ?? manifest.id,
                Version = manifest.version, Sha256 = manifest.sha256,
                Fingerprint = HashBytes(Encoding.UTF8.GetBytes(ReadSmallText(path) + "\n" + manifest.sha256.ToLowerInvariant())) };
        }

        public ModuleSelection Import(ModuleSelection current, string path, string expectedFingerprint)
        {
            var preview = Preview(path);
            if (!string.Equals(preview.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            { throw new InvalidDataException("模块包在确认后已发生变化，请重新选择并校验。"); }
            var manifest = ReadManifest(path);
            var token = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(_dataRoot, "packages", token);
            EnsureNoReparse(directory);
            Directory.CreateDirectory(directory);
            // V1 deliberately accepts a manifest plus one DLL only; no scripts, archives or dependency copying.
            File.Copy(path, Path.Combine(directory, "plugin.json"), false);
            File.Copy(Path.Combine(manifest.SourceDirectory, manifest.entryAssembly),
                Path.Combine(directory, manifest.entryAssembly), false);
            var copied = Preview(Path.Combine(directory, "plugin.json"));
            if (copied.Fingerprint != expectedFingerprint) { throw new InvalidDataException("复制校验失败；未更改启用配置。"); }
            var next = Clone(current);
            next.packages[manifest.id] = token;
            Save(next); // New packages remain disabled; existing enabled modules are updated at next start.
            return next;
        }

        public static PluginManifest ReadManifest(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var manifest = new JavaScriptSerializer().Deserialize<PluginManifest>(ReadSmallText(fullPath));
            if (manifest == null) { throw new InvalidDataException("Empty plugin manifest: " + fullPath); }
            manifest.SourceDirectory = Path.GetDirectoryName(fullPath);
            manifest.Validate();
            return manifest;
        }

        public static string VerifyAssembly(PluginManifest manifest)
        {
            var path = Path.Combine(manifest.SourceDirectory, manifest.entryAssembly);
            EnsureNoReparse(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxAssemblyBytes)
            { throw new InvalidDataException("模块 DLL 不存在或超过 16 MiB：" + manifest.id); }
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
            {
                var actual = Hex(hash.ComputeHash(stream));
                if (!string.Equals(actual, manifest.sha256, StringComparison.OrdinalIgnoreCase))
                { throw new InvalidDataException("SHA-256 校验失败：" + manifest.id); }
            }
            var name = AssemblyName.GetAssemblyName(path).Name; // Metadata only, not Assembly.Load.
            if (name.Equals("TriLink.Plugin.Abstractions", StringComparison.OrdinalIgnoreCase)
                || name.Equals("TriLink.PluginHost", StringComparison.OrdinalIgnoreCase)
                || name.Equals("TriLink.MinClient", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("模块不能替换宿主或框架程序集：" + name); }
            return name;
        }

        private static void ValidateSelection(ModuleSelection selection)
        {
            if (selection == null || selection.schemaVersion != 1 || selection.enabled == null
                || selection.packages == null || selection.enabled.Length > 64 || selection.packages.Count > 64)
            { throw new InvalidDataException("Invalid module selection. Use --safe-mode to restore built-ins."); }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in selection.enabled)
            {
                if (!ValidId(id) || !ids.Add(id)) { throw new InvalidDataException("Invalid or duplicate enabled module id."); }
            }
            foreach (var pair in selection.packages)
            {
                if (!ValidId(pair.Key) || pair.Value == null || !Regex.IsMatch(pair.Value, @"\A[0-9a-f]{32}\z"))
                { throw new InvalidDataException("Invalid package directory token."); }
            }
        }

        private static bool ValidId(string id)
        { return id != null && id.Length <= 100 && Regex.IsMatch(id, @"\A[a-z0-9]+(?:[.-][a-z0-9]+)*\z"); }

        private static string ReadSmallText(string path)
        {
            EnsureNoReparse(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxManifestBytes) { throw new InvalidDataException("配置不存在或超过 64 KiB：" + path); }
            return File.ReadAllText(path);
        }

        private static void EnsureNoReparse(string path)
        {
            for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                { throw new InvalidDataException("模块路径不能经过链接或重解析点：" + current); }
            }
        }

        private static string HashBytes(byte[] bytes)
        { using (var hash = SHA256.Create()) { return Hex(hash.ComputeHash(bytes)); } }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
