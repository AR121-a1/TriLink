using System;
using System.Collections.Generic;
using TriLink.Plugin;

namespace TriLink.PluginHost
{
    public sealed class PluginServices : IPluginServices
    {
        private const string HostOwner = "$host";
        private readonly object _sync = new object();
        private readonly Dictionary<Type, ServiceRegistration> _services =
            new Dictionary<Type, ServiceRegistration>();

        public void Register<TService>(TService service) where TService : class
        {
            RegisterOwned(HostOwner, service);
        }

        public TService GetRequired<TService>() where TService : class
        {
            TService service;
            if (!TryGet(out service))
            {
                throw new InvalidOperationException(
                    "Required plugin service is unavailable: " + typeof(TService).FullName);
            }

            return service;
        }

        public bool TryGet<TService>(out TService service) where TService : class
        {
            lock (_sync)
            {
                ServiceRegistration registration;
                if (_services.TryGetValue(typeof(TService), out registration))
                {
                    service = (TService)registration.Value;
                    return true;
                }
            }

            service = null;
            return false;
        }

        internal IDisposable RegisterOwned<TService>(string ownerId, TService service)
            where TService : class
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("Service owner is required.", nameof(ownerId));
            }
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            var serviceType = typeof(TService);
            var registration = new ServiceRegistration(ownerId, service);
            lock (_sync)
            {
                ServiceRegistration existing;
                if (_services.TryGetValue(serviceType, out existing))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "Service {0} is already provided by {1}; {2} cannot replace it.",
                            serviceType.FullName,
                            existing.OwnerId,
                            ownerId));
                }

                _services.Add(serviceType, registration);
            }

            return new ServiceLease(this, serviceType, registration);
        }

        private void Remove(Type serviceType, ServiceRegistration expected)
        {
            lock (_sync)
            {
                ServiceRegistration current;
                if (_services.TryGetValue(serviceType, out current)
                    && ReferenceEquals(current, expected))
                {
                    _services.Remove(serviceType);
                }
            }
        }

        private sealed class ServiceRegistration
        {
            public ServiceRegistration(string ownerId, object value)
            {
                OwnerId = ownerId;
                Value = value;
            }

            public string OwnerId { get; private set; }

            public object Value { get; private set; }
        }

        private sealed class ServiceLease : IDisposable
        {
            private readonly PluginServices _owner;
            private readonly Type _serviceType;
            private ServiceRegistration _registration;

            public ServiceLease(
                PluginServices owner,
                Type serviceType,
                ServiceRegistration registration)
            {
                _owner = owner;
                _serviceType = serviceType;
                _registration = registration;
            }

            public void Dispose()
            {
                var registration = _registration;
                if (registration == null)
                {
                    return;
                }

                _registration = null;
                _owner.Remove(_serviceType, registration);
            }
        }
    }
}
