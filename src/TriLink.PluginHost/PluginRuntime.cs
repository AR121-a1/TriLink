using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using TriLink.Plugin;

namespace TriLink.PluginHost
{
    public sealed class HostEnvironment : IHostEnvironment
    {
        public HostEnvironment(
            string baseDirectory,
            IEnumerable<string> arguments,
            string profileName = "desktop")
        {
            BaseDirectory = Path.GetFullPath(baseDirectory);
            Arguments = (arguments ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
            ProfileName = string.IsNullOrWhiteSpace(profileName)
                ? "desktop"
                : profileName;
            DemoMode = Arguments.Any(
                value => string.Equals(value, "--demo", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "--screenshot", StringComparison.OrdinalIgnoreCase));
        }

        public string BaseDirectory { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; }

        public bool DemoMode { get; private set; }

        public string ProfileName { get; private set; }
    }

    public sealed class PluginRuntime : IDisposable, IPluginCatalog
    {
        private readonly List<LoadedPlugin> _loaded = new List<LoadedPlugin>();
        private readonly List<PluginDescriptor> _descriptors = new List<PluginDescriptor>();
        private readonly Action<string> _log;
        private bool _started;
        private bool _disposed;

        private PluginRuntime(IHostEnvironment environment, Action<string> log)
        {
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _log = log ?? (_ => { });
            Services = new PluginServices();
            Services.Register<IPluginCatalog>(this);
        }

        public IHostEnvironment Environment { get; private set; }

        public PluginServices Services { get; private set; }

        public IReadOnlyList<PluginDescriptor> Plugins
        {
            get
            {
                return _descriptors
                    .Select(CloneDescriptor)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public static PluginRuntime LoadFromDirectory(
            string pluginRoot,
            IHostEnvironment environment,
            Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                throw new ArgumentException("Plugin root is required.", nameof(pluginRoot));
            }

            var fullRoot = Path.GetFullPath(pluginRoot);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException("Plugin root not found: " + fullRoot);
            }

            var runtime = new PluginRuntime(environment, log);
            try
            {
                runtime.DiscoverAndConfigure(fullRoot, null);
                return runtime;
            }
            catch
            {
                runtime.Dispose();
                throw;
            }
        }

        public static PluginRuntime LoadFromProfile(
            string pluginRoot,
            string profilePath,
            IHostEnvironment environment,
            Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                throw new ArgumentException("Plugin root is required.", nameof(pluginRoot));
            }

            var fullRoot = Path.GetFullPath(pluginRoot);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException("Plugin root not found: " + fullRoot);
            }

            var profile = PluginProfile.Load(profilePath);
            if (!string.Equals(
                environment.ProfileName,
                profile.name,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    string.Format(
                        "Host requested profile {0}, but file declares {1}.",
                        environment.ProfileName,
                        profile.name));
            }

            var selected = new HashSet<string>(
                profile.PluginIds,
                StringComparer.OrdinalIgnoreCase);
            var runtime = new PluginRuntime(environment, log);
            try
            {
                runtime.DiscoverAndConfigure(fullRoot, selected);
                return runtime;
            }
            catch
            {
                runtime.Dispose();
                throw;
            }
        }

        public void StartAll()
        {
            ThrowIfDisposed();
            if (_started)
            {
                return;
            }

            var started = new List<LoadedPlugin>();
            try
            {
                foreach (var item in _loaded)
                {
                    item.Instance.Start();
                    item.Descriptor.State = PluginState.Active;
                    started.Add(item);
                    _log("[" + item.Manifest.id + "] active");
                }

                _started = true;
            }
            catch (Exception exception)
            {
                var failed = _loaded.FirstOrDefault(item => !started.Contains(item));
                if (failed != null)
                {
                    failed.Descriptor.State = PluginState.Failed;
                    failed.Descriptor.Error = exception.Message;
                }

                StopReverse(
                    _loaded.Where(
                        item => item.Descriptor.State == PluginState.Active
                            || item.Descriptor.State == PluginState.Configured
                            || item.Descriptor.State == PluginState.Failed)
                        .ToList());
                _started = false;
                throw new InvalidOperationException("Plugin start failed.", exception);
            }
        }

        public void StopAll()
        {
            if (_disposed)
            {
                return;
            }

            StopReverse(
                _loaded.Where(
                    item => item.Descriptor.State == PluginState.Active
                        || item.Descriptor.State == PluginState.Configured
                        || item.Descriptor.State == PluginState.Failed)
                    .ToList());
            _started = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopAll();
            _disposed = true;
        }

        private void DiscoverAndConfigure(
            string pluginRoot,
            ISet<string> selectedPluginIds)
        {
            var serializer = new JavaScriptSerializer();
            var manifests = new List<PluginManifest>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.GetDirectories(pluginRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(directory, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                PluginManifest manifest;
                try
                {
                    manifest = serializer.Deserialize<PluginManifest>(
                        File.ReadAllText(manifestPath));
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        "Invalid plugin manifest: " + manifestPath,
                        exception);
                }

                if (manifest == null)
                {
                    throw new InvalidDataException("Empty plugin manifest: " + manifestPath);
                }

                manifest.SourceDirectory = directory;
                manifest.Validate();
                if (!ids.Add(manifest.id))
                {
                    throw new InvalidDataException("Duplicate plugin id: " + manifest.id);
                }

                if (selectedPluginIds != null
                    && !selectedPluginIds.Contains(manifest.id))
                {
                    manifest.enabled = false;
                }

                manifests.Add(manifest);
                _descriptors.Add(CreateDescriptor(manifest));
            }

            if (manifests.Count == 0)
            {
                throw new InvalidDataException("No plugin manifests found in " + pluginRoot);
            }


            if (selectedPluginIds != null)
            {
                var discoveredIds = new HashSet<string>(
                    manifests.Select(item => item.id),
                    StringComparer.OrdinalIgnoreCase);
                var missing = selectedPluginIds
                    .Where(id => !discoveredIds.Contains(id))
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
                if (missing.Length != 0)
                {
                    throw new InvalidDataException(
                        "Profile selects missing plugins: " + string.Join(", ", missing));
                }
            }

            var descriptors = _descriptors.ToDictionary(
                item => item.Id,
                StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in PluginDependencyGraph.Order(manifests))
            {
                var descriptor = descriptors[manifest.id];
                var scope = new PluginScope();
                try
                {
                    var assemblyPath = Path.Combine(
                        manifest.SourceDirectory,
                        manifest.entryAssembly);
                    if (!File.Exists(assemblyPath))
                    {
                        throw new FileNotFoundException(
                            "Plugin assembly not found.",
                            assemblyPath);
                    }

                    VerifyAssemblyHash(manifest, assemblyPath);

                    var assembly = Assembly.LoadFrom(assemblyPath);
                    var entryType = assembly.GetType(manifest.entryType, true, false);
                    if (!typeof(ITriLinkPlugin).IsAssignableFrom(entryType))
                    {
                        throw new InvalidDataException(
                            manifest.entryType + " does not implement ITriLinkPlugin.");
                    }

                    var instance = (ITriLinkPlugin)Activator.CreateInstance(entryType);
                    var context = new PluginContext(
                        manifest.id,
                        Services,
                        Environment,
                        scope,
                        manifest,
                        _log);
                    instance.Configure(context);
                    context.AssertAllDeclaredServicesProvided();
                    descriptor.State = PluginState.Configured;
                    _loaded.Add(new LoadedPlugin(manifest, descriptor, instance, scope));
                    _log("[" + manifest.id + "] configured");
                }
                catch (Exception exception)
                {
                    try
                    {
                        scope.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        _log(
                            "[" + manifest.id + "] configure rollback failed: "
                            + cleanupException.Message);
                    }

                    descriptor.State = PluginState.Failed;
                    descriptor.Error = exception.Message;
                    throw new InvalidOperationException(
                        "Plugin configure failed: " + manifest.id,
                        exception);
                }
            }
        }

        private void StopReverse(IList<LoadedPlugin> plugins)
        {
            for (var index = plugins.Count - 1; index >= 0; index--)
            {
                var item = plugins[index];
                Exception failure = null;
                try
                {
                    item.Instance.Stop();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    _log("[" + item.Manifest.id + "] stop failed: " + exception.Message);
                }

                try
                {
                    item.Scope.Dispose();
                }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(failure, exception);
                    _log("[" + item.Manifest.id + "] cleanup failed: " + exception.Message);
                }

                item.Descriptor.State = failure == null
                    ? PluginState.Stopped
                    : PluginState.Failed;
                item.Descriptor.Error = failure == null ? null : failure.Message;
                if (failure == null)
                {
                    _log("[" + item.Manifest.id + "] stopped and effects unwound");
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PluginRuntime));
            }
        }

        private static void VerifyAssemblyHash(
            PluginManifest manifest,
            string assemblyPath)
        {
            string actual;
            using (var stream = File.OpenRead(assemblyPath))
            using (var algorithm = SHA256.Create())
            {
                actual = BitConverter.ToString(algorithm.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }

            if (!string.Equals(actual, manifest.sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    string.Format(
                        "Plugin {0} failed SHA-256 verification. Expected {1}, got {2}.",
                        manifest.id,
                        manifest.sha256,
                        actual));
            }
        }

        private static PluginDescriptor CreateDescriptor(PluginManifest manifest)
        {
            return new PluginDescriptor
            {
                Id = manifest.id,
                DisplayName = string.IsNullOrWhiteSpace(manifest.displayName)
                    ? manifest.id
                    : manifest.displayName,
                Version = manifest.version,
                SourceDirectory = manifest.SourceDirectory,
                Capabilities = manifest.capabilities.ToList().AsReadOnly(),
                RequiredServices = manifest.requiresServices.ToList().AsReadOnly(),
                ProvidedServices = manifest.providesServices.ToList().AsReadOnly(),
                State = manifest.enabled ? PluginState.Discovered : PluginState.Disabled,
            };
        }

        private static PluginDescriptor CloneDescriptor(PluginDescriptor source)
        {
            return new PluginDescriptor
            {
                Id = source.Id,
                DisplayName = source.DisplayName,
                Version = source.Version,
                SourceDirectory = source.SourceDirectory,
                Capabilities = source.Capabilities.ToList().AsReadOnly(),
                RequiredServices = source.RequiredServices.ToList().AsReadOnly(),
                ProvidedServices = source.ProvidedServices.ToList().AsReadOnly(),
                State = source.State,
                Error = source.Error,
            };
        }

        private sealed class PluginContext : IPluginContext
        {
            private readonly Action<string> _log;
            private readonly PluginServices _services;
            private readonly PluginScope _scope;
            private readonly HashSet<string> _requiredServices;
            private readonly HashSet<string> _declaredProviders;
            private readonly HashSet<string> _actualProviders = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            public PluginContext(
                string pluginId,
                PluginServices services,
                IHostEnvironment environment,
                PluginScope scope,
                PluginManifest manifest,
                Action<string> log)
            {
                PluginId = pluginId;
                Environment = environment;
                _services = services;
                _scope = scope;
                _requiredServices = new HashSet<string>(
                    manifest.requiresServices,
                    StringComparer.OrdinalIgnoreCase);
                _declaredProviders = new HashSet<string>(
                    manifest.providesServices,
                    StringComparer.OrdinalIgnoreCase);
                _log = log;
            }

            public string PluginId { get; private set; }

            public IHostEnvironment Environment { get; private set; }

            public TService GetRequired<TService>() where TService : class
            {
                var serviceName = PluginServiceContract.GetName<TService>();
                if (!_requiredServices.Contains(serviceName))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "Plugin {0} accessed undeclared service {1}.",
                            PluginId,
                            serviceName));
                }

                return _services.GetRequired<TService>();
            }

            public void Provide<TService>(TService service) where TService : class
            {
                var serviceName = PluginServiceContract.GetName<TService>();
                if (!_declaredProviders.Contains(serviceName))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "Plugin {0} provided undeclared service {1}.",
                            PluginId,
                            serviceName));
                }

                var lease = _services.RegisterOwned(PluginId, service);
                _scope.Add(lease.Dispose);
                _actualProviders.Add(serviceName);
            }

            public void Defer(Action cleanup)
            {
                _scope.Add(cleanup);
            }

            public void Log(string message)
            {
                _log("[" + PluginId + "] " + message);
            }

            public void AssertAllDeclaredServicesProvided()
            {
                var missing = _declaredProviders
                    .Where(service => !_actualProviders.Contains(service))
                    .OrderBy(service => service, StringComparer.Ordinal)
                    .ToArray();
                if (missing.Length != 0)
                {
                    throw new InvalidOperationException(
                        "Plugin " + PluginId + " did not provide declared services: "
                        + string.Join(", ", missing));
                }
            }
        }

        private sealed class LoadedPlugin
        {
            public LoadedPlugin(
                PluginManifest manifest,
                PluginDescriptor descriptor,
                ITriLinkPlugin instance,
                PluginScope scope)
            {
                Manifest = manifest;
                Descriptor = descriptor;
                Instance = instance;
                Scope = scope;
            }

            public PluginManifest Manifest { get; private set; }

            public PluginDescriptor Descriptor { get; private set; }

            public ITriLinkPlugin Instance { get; private set; }

            public PluginScope Scope { get; private set; }
        }

        private sealed class PluginScope : IDisposable
        {
            private readonly object _sync = new object();
            private readonly List<Action> _cleanups = new List<Action>();
            private bool _disposed;

            public void Add(Action cleanup)
            {
                if (cleanup == null)
                {
                    throw new ArgumentNullException(nameof(cleanup));
                }

                lock (_sync)
                {
                    if (_disposed)
                    {
                        throw new ObjectDisposedException(nameof(PluginScope));
                    }

                    _cleanups.Add(cleanup);
                }
            }

            public void Dispose()
            {
                List<Action> cleanups;
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    cleanups = _cleanups.ToList();
                    _cleanups.Clear();
                }

                var failures = new List<Exception>();
                for (var index = cleanups.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        cleanups[index]();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                if (failures.Count != 0)
                {
                    throw new AggregateException("One or more plugin effects failed to unwind.", failures);
                }
            }
        }
    }
}
