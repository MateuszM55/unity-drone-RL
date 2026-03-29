/// <summary>
/// Immutable snapshot of all per-step reward components produced by
/// <see cref="DroneRewardEvaluator"/>. Consumed by <see cref="DroneTelemetry"/>
/// for logging / debug and by the agent for <c>AddReward</c>.
/// </summary>
public struct RewardStepSummary
{
    public float DeltaDistance;
    public float Proximity;
    public float Energy;
    public float Smoothness;
    public float Tilt;
    public float AngularVelocity;
    public float VelocityAlignment;
    public float Time;

    public float Total => DeltaDistance + Proximity + Energy + Smoothness
                        + Tilt + AngularVelocity + VelocityAlignment + Time;

    public RewardStepSummary(float deltaDistance, float proximity, float energy,
        float smoothness, float tilt, float angularVelocity,
        float velocityAlignment, float time)
    {
        DeltaDistance = deltaDistance;
        Proximity = proximity;
        Energy = energy;
        Smoothness = smoothness;
        Tilt = tilt;
        AngularVelocity = angularVelocity;
        VelocityAlignment = velocityAlignment;
        Time = time;
    }
}
