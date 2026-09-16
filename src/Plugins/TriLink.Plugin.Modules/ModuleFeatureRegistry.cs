using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TriLink.Plugin;

namespace TriLink.Plugins.Modules
{
    // Registration and view creation occur on the desktop STA. No polling or pre-created views.
    public sealed class ModuleFeatureRegistry : IModuleFeatureRegistry, IDisposable
    {
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _disposed;
        public IReadOnlyList<ModuleFeature> Features
        {
            get { return _entries.Values.Select(item => Copy(item.Feature)).OrderBy(item => item.Title).ToList().AsReadOnly(); }
        }
        public IDisposable Register(ModuleFeature feature, Func<Control> createView)
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(ModuleFeatureRegistry)); }
            if (feature == null || string.IsNullOrWhiteSpace(feature.Id) || string.IsNullOrWhiteSpace(feature.ModuleId)
                || string.IsNullOrWhiteSpace(feature.Title) || createView == null)
            { throw new ArgumentException("A module feature requires identity, title and view factory."); }
            if (_entries.ContainsKey(feature.Id)) { throw new InvalidOperationException("Duplicate feature: " + feature.Id); }
            var entry = new Entry { Feature = Copy(feature), Factory = createView };
            _entries.Add(feature.Id, entry);
            return new Lease(() => _entries.Remove(entry.Feature.Id));
        }
        public Control CreateView(string featureId)
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(ModuleFeatureRegistry)); }
            Entry entry;
            if (!_entries.TryGetValue(featureId, out entry)) { throw new InvalidOperationException("功能未加载：" + featureId); }
            return entry.Factory() ?? throw new InvalidOperationException("模块未返回功能视图。");
        }
        public void Dispose() { _entries.Clear(); _disposed = true; }
        private static ModuleFeature Copy(ModuleFeature value)
        { return new ModuleFeature { Id = value.Id, ModuleId = value.ModuleId, Title = value.Title, Description = value.Description }; }
        private sealed class Entry { public ModuleFeature Feature; public Func<Control> Factory; }
        private sealed class Lease : IDisposable
        {
            private Action _release;
            public Lease(Action release) { _release = release; }
            public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
        }
    }
}
