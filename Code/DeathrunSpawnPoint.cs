[Title( "Deathrun Spawn Point" )]
[Category( "Deathrun" )]
[Icon( "place" )]
public sealed class DeathrunSpawnPoint : Component
{
	[Property] public bool EnabledForSpawning { get; set; } = true;
	[Property] public int Priority { get; set; } = 0;

	public bool IsValidSpawnPoint => EnabledForSpawning && GameObject.IsValid() && GameObject.Active;
}
