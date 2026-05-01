[Title( "Deathrun Player" )]
[Category( "Deathrun" )]
[Icon( "person" )]
public sealed class DeathrunPlayer : Component
{
	private Connection _owner;

	/// <summary>
	/// The connection that owns this networked player object.
	/// Falls back to the GameObject network owner so proxy copies can still report a readable owner.
	/// </summary>
	public Connection Owner => _owner ?? GameObject?.Network.Owner;

	public string OwnerName => Owner?.DisplayName ?? "Unowned";
	public string OwnerId => GetOwnerId();
	public bool HasOwner => Owner is not null;

	public void Initialize( Connection owner )
	{
		_owner = owner;

		if ( owner is null )
		{
			Log.Warning( $"DeathrunPlayer '{GameObject.Name}' initialized without an owner connection." );
			return;
		}

		Log.Info( $"DeathrunPlayer '{GameObject.Name}' initialized for {OwnerName} ({OwnerId})." );
	}

	public bool IsOwnedBy( Connection connection )
	{
		if ( connection is null )
			return false;

		var owner = Owner;

		if ( owner is null )
			return GameObject.Network.OwnerId == connection.Id;

		return owner == connection || owner.Id == connection.Id;
	}

	private string GetOwnerId()
	{
		var owner = Owner;

		if ( owner is not null )
			return owner.Id.ToString();

		if ( GameObject.IsValid() )
			return GameObject.Network.OwnerId.ToString();

		return "none";
	}
}
