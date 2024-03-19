// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using k8s;
using k8s.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Yarp.Kubernetes.Controller.Services;

namespace Yarp.Kubernetes.Controller.Caching;

/// <summary>
/// Per-namespace cache data. Implicitly scopes name-based lookups to same namespace. Also
/// intended to make updates faster because cross-reference dictionaries are not cluster-wide.
/// </summary>
public class NamespaceCache
{
    private readonly object _sync = new object();
    private readonly Dictionary<string, ImmutableList<string>> _ingressToServiceNames = new Dictionary<string, ImmutableList<string>>();
    private readonly Dictionary<string, ImmutableList<string>> _serviceToIngressNames = new Dictionary<string, ImmutableList<string>>();
    private readonly Dictionary<string, IngressData> _ingressData = new Dictionary<string, IngressData>();
    private readonly Dictionary<string, ServiceData> _serviceData = new Dictionary<string, ServiceData>();
    private readonly Dictionary<string, PodData> _podData = new Dictionary<string, PodData>();
    private readonly Dictionary<string, Endpoints> _endpointsDataByService = new Dictionary<string, Endpoints>();
    private readonly Dictionary<string, Endpoints> _endpointsDataByIp = new Dictionary<string, Endpoints>();

    public void Update(WatchEventType eventType, V1Ingress ingress)
    {
        if (ingress is null)
        {
            throw new ArgumentNullException(nameof(ingress));
        }

        var serviceNames = ImmutableList<string>.Empty;

        if (eventType == WatchEventType.Added || eventType == WatchEventType.Modified)
        {
            // If the ingress exists, list out the related services
            var spec = ingress.Spec;
            var defaultBackend = spec?.DefaultBackend;
            var defaultService = defaultBackend?.Service;
            if (!string.IsNullOrEmpty(defaultService?.Name))
            {
                serviceNames = serviceNames.Add(defaultService.Name);
            }

            foreach (var rule in spec.Rules ?? Enumerable.Empty<V1IngressRule>())
            {
                var http = rule.Http;
                foreach (var path in http.Paths ?? Enumerable.Empty<V1HTTPIngressPath>())
                {
                    var backend = path.Backend;
                    var service = backend.Service;

                    if (!serviceNames.Contains(service.Name))
                    {
                        serviceNames = serviceNames.Add(service.Name);
                    }
                }
            }
        }

        var ingressName = ingress.Name();
        lock (_sync)
        {
            var serviceNamesPrevious = ImmutableList<string>.Empty;
            if (eventType == WatchEventType.Added || eventType == WatchEventType.Modified)
            {
                // If the ingress exists then remember details

                _ingressData[ingressName] = new IngressData(ingress);

                if (_ingressToServiceNames.TryGetValue(ingressName, out serviceNamesPrevious))
                {
                    _ingressToServiceNames[ingressName] = serviceNames;
                }
                else
                {
                    serviceNamesPrevious = ImmutableList<string>.Empty;
                    _ingressToServiceNames.Add(ingressName, serviceNames);
                }
            }
            else if (eventType == WatchEventType.Deleted)
            {
                // otherwise clear out details

                _ingressData.Remove(ingressName);

                if (_ingressToServiceNames.TryGetValue(ingressName, out serviceNamesPrevious))
                {
                    _ingressToServiceNames.Remove(ingressName);
                }
            }

            // update cross-reference for new ingress-to-services linkage not previously known
            foreach (var serviceName in serviceNames)
            {
                if (!serviceNamesPrevious.Contains(serviceName))
                {
                    if (_serviceToIngressNames.TryGetValue(serviceName, out var ingressNamesPrevious))
                    {
                        _serviceToIngressNames[serviceName] = _serviceToIngressNames[serviceName].Add(ingressName);
                    }
                    else
                    {
                        _serviceToIngressNames.Add(serviceName, ImmutableList<string>.Empty.Add(ingressName));
                    }
                }
            }

            // remove cross-reference for previous ingress-to-services linkage no longer present
            foreach (var serviceName in serviceNamesPrevious)
            {
                if (!serviceNames.Contains(serviceName))
                {
                    _serviceToIngressNames[serviceName] = _serviceToIngressNames[serviceName].Remove(ingressName);
                }
            }
        }
    }

    public ImmutableList<string> Update(WatchEventType eventType, V1Service service)
    {
        if (service is null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        var serviceName = service.Name();
        lock (_sync)
        {
            if (eventType == WatchEventType.Added || eventType == WatchEventType.Modified)
            {
                _serviceData[serviceName] = new ServiceData(service);
            }
            else if (eventType == WatchEventType.Deleted)
            {
                _serviceData.Remove(serviceName);
            }

            if (_serviceToIngressNames.TryGetValue(serviceName, out var ingressNames))
            {
                return ingressNames;
            }
            else
            {
                return ImmutableList<string>.Empty;
            }
        }
    }

    public ImmutableList<string> Update(WatchEventType eventType, V1Pod pod)
    {
        if (pod is null)
        {
            throw new ArgumentNullException(nameof(pod));
        }

        var podName = pod.Name();
        lock (_sync)
        {
            if (eventType == WatchEventType.Added || eventType == WatchEventType.Modified)
            {
                _podData[podName] = new PodData(pod);
            }
            else if (eventType == WatchEventType.Deleted)
            {
                _podData.Remove(podName);
            }

            var podIp = pod?.Status?.PodIP;
            if (!string.IsNullOrEmpty(podIp)
                && _endpointsDataByIp.TryGetValue(podIp, out var endpoints)
                && !string.IsNullOrEmpty(endpoints.Name)
                && _serviceToIngressNames.TryGetValue(endpoints.Name, out var ingressNames))
            {
                return ingressNames;
            }
            else
            {
                return ImmutableList<string>.Empty;
            }
        }
    }

    public void GetKeys(string ns, List<NamespacedName> keys)
    {
        if (keys is null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        lock (_sync)
        {
            foreach (var name in _ingressData.Keys)
            {
                keys.Add(new NamespacedName(ns, name));
            }
        }
    }

    public ImmutableList<string> Update(WatchEventType eventType, V1Endpoints endpoints)
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        var serviceName = endpoints.Name();
        lock (_sync)
        {
            if (_endpointsDataByService.TryGetValue(serviceName, out var oldEndpoints))
            {
                foreach (var ip in oldEndpoints.Subsets.SelectMany(x => x.Addresses.Select(ip => ip.Ip)))
                {
                    _endpointsDataByIp.Remove(ip);
                }
            }

            if (eventType == WatchEventType.Added || eventType == WatchEventType.Modified)
            {
                var newEndpoints = new Endpoints(endpoints);
                _endpointsDataByService[serviceName] = newEndpoints;

                if (endpoints.Subsets != null)
                {
                    foreach (var ip in endpoints.Subsets.SelectMany(x => x?.Addresses?.Select(ip => ip.Ip) ?? Enumerable.Empty<string>()))
                    {
                        // TODO - this does not handle an ip mapped to multiple endpoints
                        _endpointsDataByIp[ip] = newEndpoints;
                    }
                }
            }
            else if (eventType == WatchEventType.Deleted)
            {
                _endpointsDataByService.Remove(serviceName);
            }

            if (_serviceToIngressNames.TryGetValue(serviceName, out var ingressNames))
            {
                return ingressNames;
            }
            else
            {
                return ImmutableList<string>.Empty;
            }
        }
    }

    public IEnumerable<IngressData> GetIngresses()
    {
        return _ingressData.Values;
    }

    public bool IngressExists(V1Ingress ingress)
    {
        return _ingressData.ContainsKey(ingress.Name());
    }

    public bool TryLookup(NamespacedName key, out ReconcileData data)
    {
        var endspointsList = new List<Endpoints>();
        var servicesList = new List<ServiceData>();

        lock (_sync)
        {
            if (!_ingressData.TryGetValue(key.Name, out var ingress))
            {
                data = default;
                return false;
            }

            if (_ingressToServiceNames.TryGetValue(key.Name, out var serviceNames))
            {
                foreach (var serviceName in serviceNames)
                {
                    if (_serviceData.TryGetValue(serviceName, out var serviceData))
                    {
                        servicesList.Add(serviceData);
                    }

                    if (_endpointsDataByService.TryGetValue(serviceName, out var endpoints))
                    {
                        endspointsList.Add(endpoints);
                    }
                }
            }

            if (_serviceData.Count == 0)
            {
                data = default;
                return false;
            }

            // TODO filter pods list
            data = new ReconcileData(ingress, servicesList, endspointsList, _podData.Values.ToList());
            return true;
        }
    }
}
