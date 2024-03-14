// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Yarp.Kubernetes.Controller.Caching;

namespace Yarp.Kubernetes.Controller.Converters;

internal sealed class YarpIngressContext
{
    public YarpIngressContext(IngressData ingress, List<ServiceData> services, List<Endpoints> endpoints, List<PodData> podsList)
    {
        Ingress = ingress;
        Services = services;
        Endpoints = endpoints;
        PodsList = podsList;
    }

    public YarpIngressOptions Options { get; set; } = new YarpIngressOptions();
    public IngressData Ingress { get; }
    public List<ServiceData> Services { get; }
    public List<Endpoints> Endpoints { get; }
    public List<PodData> PodsList { get; }
}
