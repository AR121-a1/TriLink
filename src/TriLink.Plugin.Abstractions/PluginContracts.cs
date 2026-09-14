using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TriLink.Plugin
{
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class PluginServiceAttribute : Attribute
    {
        public PluginServiceAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Plugin service name is required.", nameof(name));
            }

            Name = name;
        }

        public string Name { get; private set; }
    }

    public static class PluginServiceIds
    {
        public const string PluginCatalog = "trilink.plugin-catalog";
        public const string RoomNetwork = "trilink.room-network";
        public const string DeviceDiscovery = "trilink.device-discovery";
        public const string SimulationControl = "trilink.simulation-control";
        public const string DesktopShell = "trilink.desktop-shell";
    }

    public static class PluginServiceContract
    {
        public static string GetName<TService>() where TService : class
        {
            return GetName(typeof(TService));
        }

        public static string GetName(Type serviceType)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            var attribute = (PluginServiceAttribute)Attribute.GetCustomAttribute(
                serviceType,
                typeof(PluginServiceAttribute));
            if (attribute == null)
            {
                throw new InvalidOperationException(
                    "Service contract has no PluginServiceAttribute: " + serviceType.FullName);
            }

            return attribute.Name;
        }
    }

    public static class PluginApi
    {
        public const string Version = "1.0";
    }

    public enum PluginState
    {
        Discovered = 0,
        Configured = 1,
        Active = 2,
        Stopped = 3,
        Disabled = 4,
        Failed = 5,
    }

    public sealed class PluginDescriptor
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string Version { get; set; }

        public string SourceDirectory { get; set; }

        public IReadOnlyList<string> Capabilities { get; set; }

        public IReadOnlyList<string> RequiredServices { get; set; }

        public IReadOnlyList<string> ProvidedServices { get; set; }

        public PluginState State { get; set; }

        public string Error { get; set; }
    }

    [PluginService(PluginServiceIds.PluginCatalog)]
    public interface IPluginCatalog
    {
        IReadOnlyList<PluginDescriptor> Plugins { get; }
    }

    public interface IHostEnvironment
    {
        string BaseDirectory { get; }

        IReadOnlyList<string> Arguments { get; }

        bool DemoMode { get; }

        string ProfileName { get; }
    }

    public interface IPluginServices
    {
        void Register<TService>(TService service) where TService : class;

        TService GetRequired<TService>() where TService : class;

        bool TryGet<TService>(out TService service) where TService : class;
    }

    public interface IPluginContext
    {
        string PluginId { get; }

        IHostEnvironment Environment { get; }

        TService GetRequired<TService>() where TService : class;

        void Provide<TService>(TService service) where TService : class;

        void Defer(Action cleanup);

        void Log(string message);
    }

    public interface ITriLinkPlugin
    {
        void Configure(IPluginContext context);

        void Start();

        void Stop();
    }

    [PluginService(PluginServiceIds.DesktopShell)]
    public interface IDesktopShell
    {
        Form CreateMainWindow();

        void SaveScreenshot(Form form, string path);

        void ExitApplication(Form form);
    }

    public static class HostWindowMessages
    {
        private const string ActivateMessageName = "TriLink.MinClient.Activate.v3";

        public static readonly int ActivateMessage =
            RegisterWindowMessage(ActivateMessageName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegisterWindowMessage(string messageName);
    }
}
