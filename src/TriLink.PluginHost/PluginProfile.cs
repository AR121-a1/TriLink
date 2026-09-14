using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TriLink.PluginHost
{
    public sealed class PluginProfile
    {
        private static readonly Regex NameRegex = new Regex(
            @"^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int schemaVersion { get; set; }

        public string name { get; set; }

        public string[] plugins { get; set; }

        public string SourcePath { get; private set; }

        public static PluginProfile Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Profile path is required.", nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Plugin profile not found.", fullPath);
            }

            PluginProfile profile;
            try
            {
                profile = new JavaScriptSerializer().Deserialize<PluginProfile>(
                    File.ReadAllText(fullPath));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Invalid plugin profile: " + fullPath, exception);
            }

            if (profile == null)
            {
                throw new InvalidDataException("Empty plugin profile: " + fullPath);
            }

            profile.SourcePath = fullPath;
            profile.Validate();
            return profile;
        }

        public void Validate()
        {
            if (schemaVersion != 1)
            {
                throw new InvalidDataException(
                    "Unsupported plugin profile schema: " + schemaVersion);
            }
            if (string.IsNullOrWhiteSpace(name) || !NameRegex.IsMatch(name))
            {
                throw new InvalidDataException("Invalid plugin profile name: " + name);
            }

            plugins = plugins ?? new string[0];
            if (plugins.Length == 0)
            {
                throw new InvalidDataException("Plugin profile is empty: " + name);
            }

            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pluginId in plugins)
            {
                if (string.IsNullOrWhiteSpace(pluginId)
                    || !NameRegex.IsMatch(pluginId)
                    || !distinct.Add(pluginId))
                {
                    throw new InvalidDataException(
                        "Invalid or duplicate plugin in profile " + name + ": " + pluginId);
                }
            }
        }

        public IReadOnlyCollection<string> PluginIds
        {
            get
            {
                return plugins.ToList().AsReadOnly();
            }
        }
    }
}
