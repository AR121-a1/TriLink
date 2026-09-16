using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TriLink.Plugin
{
    // Additive API 1.0 contracts. Optional modules declare these service dependencies.
    public sealed class ModuleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string RunningVersion { get; set; }
        public bool Running { get; set; }
        public bool EnabledNextStart { get; set; }
        public bool PendingChange { get; set; }
        public string DisableBlockedReason { get; set; }
        public string SourceDirectory { get; set; }
        public string[] Dependencies { get; set; }
        public string[] RequiredServices { get; set; }
        public string[] ProvidedServices { get; set; }
        public string[] Capabilities { get; set; }
    }

    public sealed class ModuleSnapshot
    {
        public IReadOnlyList<ModuleInfo> Modules { get; set; }
        public bool RestartRequired { get; set; }
        public bool SafeMode { get; set; }
        public string Notice { get; set; }
    }

    public sealed class ModulePackagePreview
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Fingerprint { get; set; }
        public string Sha256 { get; set; }
    }

    [PluginService(PluginServiceIds.ModuleManagement)]
    public interface IModuleManagementService
    {
        ModuleSnapshot GetSnapshot();
        ModulePackagePreview PreviewPackage(string manifestPath);
        void ImportPackage(string manifestPath, string expectedFingerprint);
        void SetEnabled(string id, bool enabled);
        void CancelPendingChanges();
        void RestoreBuiltIns();
    }

    public sealed class ModuleFeature
    {
        public string Id { get; set; }
        public string ModuleId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }

    [PluginService(PluginServiceIds.ModuleFeatures)]
    public interface IModuleFeatureRegistry
    {
        IReadOnlyList<ModuleFeature> Features { get; }
        IDisposable Register(ModuleFeature feature, Func<Control> createView);
        Control CreateView(string featureId);
    }
}
