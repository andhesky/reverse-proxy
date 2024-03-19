// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Utilities;

namespace Yarp.ReverseProxy.LoadBalancing;

internal sealed class SlowStartRandomLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly IRandomFactory _randomFactory;
    private readonly ILogger<SlowStartRandomLoadBalancingPolicy> _logger;
    private readonly SlowStartDestinationSelector _destinationSelector;

    public SlowStartRandomLoadBalancingPolicy(
        IRandomFactory randomFactory,
        ILogger<SlowStartRandomLoadBalancingPolicy> logger)
    {
        _randomFactory = randomFactory;
        _logger = logger;
        _destinationSelector = new SlowStartDestinationSelector(randomFactory, logger);
    }

    public string Name => LoadBalancingPolicies.SlowStartRandom;

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0)
        {
            return null;
        }

        return _destinationSelector.PickRandomDestination(availableDestinations);
    }
}
