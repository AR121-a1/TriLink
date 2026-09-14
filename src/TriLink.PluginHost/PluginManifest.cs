using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TriLink.Plugin;

namespace TriLink.PluginHost
{
    public sealed class PluginManifest
    {
        private static readonly Regex PluginIdRegex = new Regex(
            @"^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int schemaVersion { get; set; }

        public string id { get; set; }

        public string displayName { get; set; }

        public string version { get; set; }

        public string hostApi { get; set; }

        public bool enabled { get; set; }

        public string entryAssembly { get; set; }

        public string entryType { get; set; }

        public string sha256 { get; set; }

        public string[] dependencies { get; set; }

        public string[] requiresServices { get; set; }

        public string[] providesServices { get; set; }

        public string[] capabilities { get; set; }

        public string SourceDirectory { get; internal set; }

        public void Validate()
        {
            if (schemaVersion != 1)
            {
                throw new InvalidDataException("Unsupported plugin manifest schema: " + schemaVersion);
            }

            if (string.IsNullOrWhiteSpace(id) || !PluginIdRegex.IsMatch(id))
            {
                throw new InvalidDataException("Invalid plugin id: " + id);
            }

            System.Version parsedVersion;
            if (string.IsNullOrWhiteSpace(version)
                || !System.Version.TryParse(version, out parsedVersion))
            {
                throw new InvalidDataException("Invalid version for plugin " + id + ": " + version);
            }

            if (!string.Equals(hostApi, PluginApi.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    string.Format(
                        "Plugin {0} requires host API {1}; this host provides {2}.",
                        id,
                        hostApi,
                        PluginApi.Version));
            }

            if (string.IsNullOrWhiteSpace(entryAssembly)
                || !entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetFileName(entryAssembly),
                    entryAssembly,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsafe entryAssembly for plugin " + id + ".");
            }

            if (string.IsNullOrWhiteSpace(entryType))
            {
                throw new InvalidDataException("Plugin " + id + " has no entryType.");
            }

            if ((!string.IsNullOrWhiteSpace(SourceDirectory)
                    || !string.IsNullOrWhiteSpace(sha256))
                && (string.IsNullOrWhiteSpace(sha256)
                    || !Regex.IsMatch(
                        sha256,
                        "^[0-9a-fA-F]{64}$",
                        RegexOptions.CultureInvariant)))
            {
                throw new InvalidDataException("Invalid SHA-256 for plugin " + id + ".");
            }

            dependencies = dependencies ?? new string[0];
            requiresServices = requiresServices ?? new string[0];
            providesServices = providesServices ?? new string[0];
            capabilities = capabilities ?? new string[0];
            var distinctDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency)
                    || !PluginIdRegex.IsMatch(dependency)
                    || !distinctDependencies.Add(dependency))
                {
                    throw new InvalidDataException(
                        "Invalid or duplicate dependency in plugin " + id + ": " + dependency);
                }

                if (string.Equals(dependency, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Plugin cannot depend on itself: " + id);
                }
            }

            if (capabilities.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("Plugin " + id + " has an empty capability.");
            }

            ValidateServiceNames(requiresServices, "required");
            ValidateServiceNames(providesServices, "provided");
        }

        private void ValidateServiceNames(IEnumerable<string> names, string role)
        {
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)
                    || !PluginIdRegex.IsMatch(name)
                    || !distinct.Add(name))
                {
                    throw new InvalidDataException(
                        string.Format(
                            "Invalid or duplicate {0} service in plugin {1}: {2}",
                            role,
                            id,
                            name));
                }
            }
        }
    }

    public static class PluginDependencyGraph
    {
        public static IReadOnlyList<PluginManifest> Order(
            IEnumerable<PluginManifest> manifests)
        {
            if (manifests == null)
            {
                throw new ArgumentNullException(nameof(manifests));
            }

            var enabled = manifests.Where(item => item.enabled).ToList();
            var byId = new Dictionary<string, PluginManifest>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in enabled)
            {
                manifest.Validate();
                if (byId.ContainsKey(manifest.id))
                {
                    throw new InvalidDataException("Duplicate plugin id: " + manifest.id);
                }

                byId.Add(manifest.id, manifest);
            }

            var result = new List<PluginManifest>();
            var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var providers = new Dictionary<string, PluginManifest>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in enabled)
            {
                foreach (var service in manifest.providesServices)
                {
                    PluginManifest existing;
                    if (providers.TryGetValue(service, out existing))
                    {
                        throw new InvalidDataException(
                            string.Format(
                                "Service {0} has multiple providers: {1}, {2}.",
                                service,
                                existing.id,
                                manifest.id));
                    }

                    providers.Add(service, manifest);
                }
            }

            var hostServices = new HashSet<string>(
                new[] { PluginServiceIds.PluginCatalog },
                StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in enabled.OrderBy(item => item.id, StringComparer.Ordinal))
            {
                Visit(manifest, byId, providers, hostServices, states, result);
            }

            return result.AsReadOnly();
        }

        private static void Visit(
            PluginManifest manifest,
            IReadOnlyDictionary<string, PluginManifest> byId,
            IReadOnlyDictionary<string, PluginManifest> providers,
            ISet<string> hostServices,
            IDictionary<string, int> states,
            ICollection<PluginManifest> result)
        {
            int state;
            if (states.TryGetValue(manifest.id, out state))
            {
                if (state == 1)
                {
                    throw new InvalidDataException(
                        "Plugin dependency cycle includes: " + manifest.id);
                }

                if (state == 2)
                {
                    return;
                }
            }

            states[manifest.id] = 1;
            foreach (var dependencyId in manifest.dependencies)
            {
                PluginManifest dependency;
                if (!byId.TryGetValue(dependencyId, out dependency))
                {
                    throw new InvalidDataException(
                        string.Format(
                            "Plugin {0} requires missing or disabled plugin {1}.",
                            manifest.id,
                            dependencyId));
                }

                Visit(dependency, byId, providers, hostServices, states, result);
            }

            foreach (var service in manifest.requiresServices)
            {
                if (hostServices.Contains(service))
                {
                    continue;
                }

                PluginManifest provider;
                if (!providers.TryGetValue(service, out provider))
                {
                    throw new InvalidDataException(
                        string.Format(
                            "Plugin {0} requires missing service {1}.",
                            manifest.id,
                            service));
                }

                Visit(provider, byId, providers, hostServices, states, result);
            }

            states[manifest.id] = 2;
            result.Add(manifest);
        }
    }
}
