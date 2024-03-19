// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Utilities;

namespace Yarp.ReverseProxy.LoadBalancing;

internal sealed class SlowStartDestinationSelector
{
    private const int SlowStartWindowSeconds = 200; // TODO configurable

    private readonly IRandomFactory _randomFactory;
    private readonly ILogger _logger;

    public SlowStartDestinationSelector(IRandomFactory randomFactory, ILogger logger)
    {
        _randomFactory = randomFactory;
        _logger = logger;
    }

    public DestinationState? PickRandomDestination(IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0)
        {
            return null;
        }
        var computedWeights = ComputeWeights(availableDestinations);

        var destinationIndex = PickRandomDestination(computedWeights);
        return availableDestinations[destinationIndex];
    }

    public int PickRandomDestination((List<(DestinationState destination, int weight)> destinationWeights, int totalWeight) computedWeights)
    {
        var destinationWeights = computedWeights.destinationWeights;
        var totalWeight = computedWeights.totalWeight;

        var random = _randomFactory.CreateRandomInstance();
        var chosenInstance = random.Next(totalWeight);

        _logger.LogInformation("Picking destination {0} from {1}. TotalWeight={2}", chosenInstance, destinationWeights.Select(x => (x.destination.DestinationId, x.weight)).ToArray(), totalWeight);

        for (var i = 0; i < destinationWeights.Count - 1; i++)
        {
            var destinationWeight = destinationWeights[i];

            if (chosenInstance < destinationWeight.weight)
            {
                _logger.LogInformation("Picked {0}", i);
                return i;
            }

            chosenInstance -= destinationWeight.weight;
        }

        _logger.LogInformation("Picked last one ({0})", destinationWeights.Count - 1);
        return destinationWeights.Count - 1;
    }

    public (List<(DestinationState destination, int weight)> destinationWeights, int totalWeight) ComputeWeights(IReadOnlyList<DestinationState> availableDestinations)
    {
        var now = DateTime.UtcNow;

        // TODO This should all be computed ahead of time and likely needs to work with any arbitrary load balancing policy
        List<(DestinationState destination, int weight)> destinationWeights = availableDestinations.Select(x => (x, ComputeWeight(x, now))).ToList();
        var totalWeight = destinationWeights.Select(x => x.weight).Sum();

        return (destinationWeights, totalWeight);
    }

    private int ComputeWeight(DestinationState destination, DateTime now)
    {
        return ComputeWeight(destination.Health.LastHealthyStateTransition, now);
    }


    private static int ComputeWeight(DateTime? lastHealthStateTransition, DateTime now)
    {
        var secondsSinceReady = lastHealthStateTransition == null ? SlowStartWindowSeconds : Math.Min((int)(now - lastHealthStateTransition.Value).TotalSeconds, SlowStartWindowSeconds);
        return secondsSinceReady;
    }
}
;
