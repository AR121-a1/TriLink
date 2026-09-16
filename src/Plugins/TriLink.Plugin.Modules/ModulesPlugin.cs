using System;
using System.Linq;
using TriLink.Plugin;
using TriLink.PluginHost;

namespace TriLink.Plugins.Modules
{
    public sealed class ModulesPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            var features = new ModuleFeatureRegistry();
            context.Defer(features.Dispose);
            context.Provide<IModuleFeatureRegistry>(features);
            context.Provide<IModuleManagementService>(new ModuleManagementService(
                new ModuleStore(context.Environment), context.GetRequired<IPluginCatalog>(),
                context.Environment.Arguments.Any(arg => string.Equals(arg, "--safe-mode", StringComparison.OrdinalIgnoreCase))));
            context.Log("扩展管理服务与按需功能注册表已就绪；变更于下次启动生效。");
        }
        public void Start() { }
        public void Stop() { }
    }

    public sealed class ModuleManagementService : IModuleManagementService
    {
        private readonly ModuleStore _store;
        private readonly IPluginCatalog _catalog;
        private readonly bool _safeMode;
        private readonly ModuleSelection _started;
        private ModuleSelection _desired;
        private bool _recoverySaved;

        public ModuleManagementService(ModuleStore store, IPluginCatalog catalog, bool safeMode)
        {
            _store = store;
            _catalog = catalog;
            _safeMode = safeMode;
            _started = store.ReadSelection(safeMode);
            _desired = ModuleStore.Clone(_started);
        }

        public ModuleSnapshot GetSnapshot()
        {
            var running = _catalog.Plugins.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var modules = _store.Resolve(_desired).Select(manifest =>
            {
                PluginDescriptor current;
                running.TryGetValue(manifest.id, out current);
                var active = current != null && current.State == PluginState.Active;
                return new ModuleInfo { Id = manifest.id, Name = manifest.displayName ?? manifest.id,
                    Version = manifest.version, RunningVersion = active ? current.Version : null,
                    Running = active, EnabledNextStart = manifest.enabled,
                    PendingChange = active != manifest.enabled || (active &&
                        !string.Equals(current.SourceDirectory, manifest.SourceDirectory, StringComparison.OrdinalIgnoreCase)),
                    DisableBlockedReason = manifest.enabled ? _store.DisableBlockedReason(_desired, manifest.id) : null,
                    SourceDirectory = manifest.SourceDirectory, Dependencies = manifest.dependencies.ToArray(),
                    RequiredServices = manifest.requiresServices.ToArray(), ProvidedServices = manifest.providesServices.ToArray(),
                    Capabilities = manifest.capabilities.ToArray() };
            }).ToList().AsReadOnly();
            return new ModuleSnapshot { Modules = modules, SafeMode = _safeMode,
                RestartRequired = !ModuleStore.Equivalent(_started, _desired) || _recoverySaved,
                Notice = _safeMode ? (_recoverySaved ? "已保存恢复配置。请从托盘退出，再正常启动。"
                    : "安全恢复模式：忽略自定义配置，仅运行内置模块。请选择“恢复内置”。")
                    : "启停与更新仅在下次启动生效；X 隐藏到托盘不等于重启。" };
        }

        public ModulePackagePreview PreviewPackage(string manifestPath) { return _store.Preview(manifestPath); }
        public void ImportPackage(string manifestPath, string expectedFingerprint)
        {
            RequireEditable();
            _desired = _store.Import(_desired, manifestPath, expectedFingerprint);
        }
        public void SetEnabled(string id, bool enabled)
        {
            RequireEditable();
            if (!_store.Resolve(_desired).Any(item => item.id == id)) { throw new InvalidOperationException("未知模块：" + id); }
            var next = ModuleStore.Clone(_desired);
            next.enabled = next.enabled.Where(value => value != id).Concat(enabled ? new[] { id } : new string[0]).ToArray();
            _store.Save(next);
            _desired = next;
        }
        public void CancelPendingChanges()
        {
            RequireEditable();
            _store.Save(_started);
            _desired = ModuleStore.Clone(_started);
        }
        public void RestoreBuiltIns()
        {
            var next = _store.BuiltIns();
            _store.Save(next);
            _desired = next;
            _recoverySaved = _safeMode;
        }
        private void RequireEditable()
        {
            if (_safeMode) { throw new InvalidOperationException("安全模式仅允许恢复内置配置；恢复后请正常启动再管理模块。"); }
        }
    }
}
