/// <summary>
/// Immutable snapshot of all per-step reward components produced by
/// <see cref="DroneRewardManager"/>. Consumed by <see cref="DroneTelemetry"/>
/// for logging / debug and by the agent for <c>AddReward</c>.
/// </summary>
public struct RewardStepSummary
{
    public readonly float DeltaDistance;
    public readonly float NormalizedDeltaDistance;
    public readonly float Proximity;
    public readonly float Energy;
    public readonly float Smoothness;
    public readonly float Tilt;
    public readonly float AngularVelocity;
    public readonly float VelocityAlignment;
    public readonly float Time;
    public readonly float FastApproach;

    public float Total => DeltaDistance + NormalizedDeltaDistance + Proximity + Energy + Smoothness
                        + Tilt + AngularVelocity + VelocityAlignment + Time
                        + FastApproach;

    public RewardStepSummary(float deltaDistance, float normalizedDeltaDistance, float proximity, float energy,
        float smoothness, float tilt, float angularVelocity,
        float velocityAlignment, float time, float fastApproach = 0f)
    {
        DeltaDistance = deltaDistance;
        NormalizedDeltaDistance = normalizedDeltaDistance;
        Proximity = proximity;
        Energy = energy;
        Smoothness = smoothness;
        Tilt = tilt;
        AngularVelocity = angularVelocity;
        VelocityAlignment = velocityAlignment;
        Time = time;
        FastApproach = fastApproach;
    }
}
