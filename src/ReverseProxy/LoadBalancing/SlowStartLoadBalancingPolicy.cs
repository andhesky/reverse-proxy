// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Utilities;

namespace Yarp.ReverseProxy.LoadBalancing;

internal sealed class SlowStartLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly IRandomFactory _randomFactory;
    private readonly ILogger<SlowStartLoadBalancingPolicy> _logger;

    public SlowStartLoadBalancingPolicy(
        IRandomFactory randomFactory,
        ILogger<SlowStartLoadBalancingPolicy> logger)
    {
        _randomFactory = randomFactory;
        _logger = logger;
    }

    public string Name => LoadBalancingPolicies.SlowStart;

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;

        // TODO This should all be computed ahead of time and likely needs to work with any arbitrary load balancing policy
        List<(DestinationState destination, int weight)> destinationWeights = availableDestinations.Select(x => (x, ComputeWeight(x.Health.LastHealthyStateTransition, now))).ToList();
        var totalWeight = destinationWeights.Select(x => x.weight).Sum();

        var random = _randomFactory.CreateRandomInstance();
        var chosenInstance = random.Next(totalWeight);

        //var destinationWeightsJson = JsonSerializer.Serialize(destinationWeights, jsonTypeInfo);

        _logger.LogInformation("Picking Destinations from {0}. TotalWeight={1}", destinationWeights, totalWeight);

        for ( var i = 0; i < destinationWeights.Count - 1; i++)
        {
            var destinationWeight = destinationWeights[i];

            if (chosenInstance < destinationWeight.weight)
            {
                return destinationWeight.destination;
            }

            chosenInstance -= destinationWeight.weight;
        }

        return destinationWeights[destinationWeights.Count - 1].destination;
    }

    private const int SlowStartWindowSeconds = 200;

    private static int ComputeWeight(DateTime? lastHealthStateTransition, DateTime now)
    {
        var secondsSinceReady = lastHealthStateTransition == null ? SlowStartWindowSeconds : Math.Max((int)(now - lastHealthStateTransition.Value).TotalSeconds, SlowStartWindowSeconds);
        return secondsSinceReady;
    }
}
