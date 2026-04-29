/// <summary>
/// Immutable snapshot of all per-step reward components produced by
/// <see cref="DroneRewardManager"/>. Consumed by <see cref="DroneTelemetry"/>
/// for logging / debug and by the agent for <c>AddReward</c>.
/// </summary>
public struct RewardStepSummary
{
    public readonly float NormalizedDeltaDistance;
    public readonly float Time;
    public readonly float Restlessness;
    public readonly float YawDeviation;

    public float Total => NormalizedDeltaDistance + Time + Restlessness + YawDeviation;

    public RewardStepSummary(float normalizedDeltaDistance, float time,
        float restlessness = 0f, float yawDeviation = 0f)
    {
        NormalizedDeltaDistance = normalizedDeltaDistance;
        Time = time;
        Restlessness = restlessness;
        YawDeviation = yawDeviation;
    }
}
