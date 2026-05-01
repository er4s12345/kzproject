public struct DeathrunDamageInfo
{
	public float Amount { get; set; }
	public DeathrunDamageType DamageType { get; set; }
	public GameObject Source { get; set; }
	public Vector3 SourcePosition { get; set; }
	public Vector3 HitPosition { get; set; }
	public Vector3 Force { get; set; }
	public string Reason { get; set; }
	public bool IsLethal { get; set; }
	public bool InvalidatesRun { get; set; }

	public static DeathrunDamageInfo Create( float amount, DeathrunDamageType damageType, string reason = null )
	{
		return new DeathrunDamageInfo
		{
			Amount = amount,
			DamageType = damageType,
			Reason = reason
		};
	}
}
