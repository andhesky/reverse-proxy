// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Utilities;

namespace Yarp.ReverseProxy.LoadBalancing;

internal sealed class SlowStartPowerOfTwoChoicesLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly IRandomFactory _randomFactory;
    private readonly ILogger<SlowStartPowerOfTwoChoicesLoadBalancingPolicy> _logger;
    private readonly SlowStartDestinationSelector _destinationSelector;

    public SlowStartPowerOfTwoChoicesLoadBalancingPolicy(IRandomFactory randomFactory,
        ILogger<SlowStartPowerOfTwoChoicesLoadBalancingPolicy> logger)
    {
        _randomFactory = randomFactory;
        _logger = logger;
        _destinationSelector = new SlowStartDestinationSelector(randomFactory, logger);
    }

    public string Name => LoadBalancingPolicies.SlowStartPowerOfTwoChoices;

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> availableDestinations)
    {
        var destinationCount = availableDestinations.Count;
        if (destinationCount == 0)
        {
            return null;
        }
        
        if (destinationCount == 1)
        {
            return availableDestinations[0];
        }

        // Pick two, and then return the least busy. This avoids the effort of searching the whole list, but
        // still avoids overloading a single destination.
        var weights = _destinationSelector.ComputeWeights(availableDestinations);
        var firstIndex = _destinationSelector.PickRandomDestination(weights);
        int secondIndex;
        do
        {
            secondIndex = _destinationSelector.PickRandomDestination(weights);
        } while (firstIndex == secondIndex);

        var destinationWeights = weights.destinationWeights;
        var first = destinationWeights[firstIndex];
        var second = destinationWeights[secondIndex];

        var firstAdjustedRequestCount = second.weight * first.destination.ConcurrentRequestCount;
        var secondAdjustedRequestCount = first.weight * second.destination.ConcurrentRequestCount;
        _logger.LogInformation("Picking between {0} and {1}. adjustedRequestCounts {2} and {3}", firstIndex, secondIndex, firstAdjustedRequestCount, secondAdjustedRequestCount);

        return (firstAdjustedRequestCount <= secondAdjustedRequestCount) ? first.destination : second.destination;
    }
}
