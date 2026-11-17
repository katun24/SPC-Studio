namespace SpcStudio.Controls;

// Which of the things a press could take hold of it takes: the nearest within grabbing distance.
public static class GrabDistance
{
    // The candidate nearest the pointer within a grabbing distance, the later of two as near, or null where none is; a candidate with no distance is passed over.
    public static TCandidate? Nearest<TCandidate>(IEnumerable<TCandidate> candidates, Func<TCandidate, double?> distanceToPointer, double grabDistance) where TCandidate : struct
    {
        TCandidate? nearestCandidate = null;
        double nearestDistance = grabDistance;
        foreach (var candidate in candidates)
        {
            if (distanceToPointer(candidate) is not double distance || distance > nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearestCandidate = candidate;
        }

        return nearestCandidate;
    }
}
