using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;


/**
 * TriggerTeleport teleports the objects which entered the trigger to the specified destination if they match the filter settings.
 * NOTE: It should be based on TriggerBase or something like that, because of base logic which draws gizmos.
 */

namespace Sandbox.Components.Triggers
{
	[Category( "Triggers" ), Icon( "adjust" )] // ? wtf is this icon, where can I found which can I use? Can I use custom icons?
	public class TriggerTeleport : TriggerBase, Component.ITriggerListener
	{
		[Property]
		[Description( "Zero-out object's velocity on teleport?" )]
		bool ResetVelocity { get; set; } = false;


		[Property, Required]
		private GameObject TeleportDestination { get; set; }

		[Property]
		[Description( "If false, players with DeathrunHealth will not be teleported by this trigger. Use false for old reset-to-spawn volumes so DeathrunHealth owns respawn timing." )]
		public bool TeleportDeathrunPlayers { get; set; } = true;

		[Property]
		[Description( "If true, blocked DeathrunHealth players are killed instead of teleported, allowing DeathrunHealth's delayed respawn flow to handle them." )]
		public bool KillBlockedDeathrunPlayers { get; set; } = false;

		[Property]
		[Description( "If false, dead DeathrunHealth players will not be teleported. Leave false for Deathrun death/respawn flow." )]
		public bool TeleportDeadPlayers { get; set; } = false;

		private Vector3 _TeleportDestLocation = Vector3.Zero;

		protected bool CanTeleport(GameObject Object)
		{
			if ( !CanTrigger() ) return false;
			if ( !Object.IsValid() ) return false;
			
			var Health = Object.Components.GetInAncestorsOrSelf<DeathrunHealth>();
			bool IsDeathrunPlayer = Health.IsValid();
			bool IsDeadPlayer = Health.IsValid() && Health.IsDead;

			if ( IsDeathrunPlayer && !TeleportDeathrunPlayers )
				return false;

			if ( IsDeadPlayer && !TeleportDeadPlayers )
				return false;

			return MatchesTeleportFilter( Object );
		}

		protected override void OnAwake()
		{
			TriggerCollider ??= Components.GetOrCreate<BoxCollider>();

			if ( TriggerCollider is not null )
				TriggerCollider.IsTrigger = true;
			if ( TeleportDestination is null )
			{
				Log.Error ( $"TeleportDestination is not set for '{GameObject.Name}' - Removing..." );
				DestroyGameObject();
				
				return;
			}


			_TeleportDestLocation = TeleportDestination.WorldPosition;
			GameObject.Tags.Add( "trigger" );
			Tags.Add( "trigger" );
		}

		protected override void DrawGizmos()
		{
			if ( !TriggerCollider.IsValid() || !TeleportDestination.IsValid() )
				return;
			base.DrawGizmos();

			var DestinationLocal = WorldTransform.PointToLocal( TeleportDestination.WorldPosition ); // NOTE: Gizmos are using local space for drawing, so we need to convert the world position to local position in this case
			if ( TeleportDestination is not null && Gizmo.IsSelected )
			{

				Gizmo.Draw.Color = Color.Red;
				Gizmo.Draw.LineThickness = 5;
				Gizmo.Draw.Line( TriggerCollider.Center, DestinationLocal );
				Gizmo.Draw.LineThickness = 2.5f;
				Gizmo.Draw.LineBBox( new BBox( DestinationLocal - Vector3.One * 10.0f, DestinationLocal+ Vector3.One * 10.0f ) );
			}
		}



		void ITriggerListener.OnTriggerEnter( GameObject other )
		{
			if ( Networking.IsActive && !Networking.IsHost )
				return;

			var Target = ResolveTeleportTarget( other );

			if ( TryHandleBlockedDeathrunPlayer( Target, other ) )
				return;

			if ( Target.IsValid() && CanTeleport( Target ) )
			{
				// Check if destination has destination volume component, if so, get random position inside the volume for teleportation.
				TeleportDestinationVolume DestinstaitonVolume = TeleportDestination.Components.Get<TeleportDestinationVolume>();
				if( DestinstaitonVolume != null )
					_TeleportDestLocation = DestinstaitonVolume.GetRandomPositionFor( Target );
				else
					_TeleportDestLocation = TeleportDestination.WorldPosition;
				

				if (ResetVelocity)
				{
					var controller = Target.Components.GetInAncestorsOrSelf<PlayerController>();
					var rigidbody = controller.IsValid() && controller.Body.IsValid()
						? controller.Body
						: Target.Components.GetInAncestorsOrSelf<Rigidbody>();

					if ( rigidbody.IsValid() )
						rigidbody.Velocity = Vector3.Zero;
				}

				Target.WorldPosition = _TeleportDestLocation;

				if ( Target.Network.Active )
					Target.Network.ClearInterpolation();

				LastTriggerTime = Time.Now;

				if ( OnlyOnce ) DestroyGameObject();
			}
		}

		private bool TryHandleBlockedDeathrunPlayer( GameObject Target, GameObject TriggeringObject )
		{
			if ( !Target.IsValid() || !CanTrigger() )
				return false;

			var Health = Target.Components.GetInAncestorsOrSelf<DeathrunHealth>();

			if ( !Health.IsValid() || TeleportDeathrunPlayers )
				return false;

			if ( !MatchesTeleportFilter( Target ) )
				return false;

			if ( KillBlockedDeathrunPlayers && !Health.IsDead )
			{
				var hitPosition = TriggeringObject.IsValid() ? TriggeringObject.WorldPosition : Target.WorldPosition;

				if ( Health.TakeDamage( new DeathrunDamageInfo
				{
					Amount = Health.MaxHealth,
					DamageType = DeathrunDamageType.KillZone,
					Source = GameObject,
					SourcePosition = WorldPosition,
					HitPosition = hitPosition,
					Reason = $"Entered teleport trigger '{GameObject.Name}'",
					IsLethal = true,
					InvalidatesRun = true
				} ) )
				{
					LastTriggerTime = Time.Now;
				}
			}

			return true;
		}

		private bool MatchesTeleportFilter( GameObject Object )
		{
			if ( !Object.IsValid() )
				return false;

			bool IsPlayer = Object.Components.GetInAncestorsOrSelf<PlayerController>().IsValid();
			bool IsPhysics = Object.Components.GetInAncestorsOrSelf<Rigidbody>().IsValid();
			bool TagFilterPassed = true;

			if ( TagFilter.Any() )
				TagFilterPassed = Object.Tags.HasAny( TagFilter );

			return TagFilterPassed && ((TeleportPlayers && IsPlayer) || ( TeleportPhysics && IsPhysics ));
		}

		private static GameObject ResolveTeleportTarget( GameObject other )
		{
			if ( !other.IsValid() )
				return null;

			var Health = other.Components.GetInAncestorsOrSelf<DeathrunHealth>();

			if ( Health.IsValid() )
				return Health.GameObject;

			var PlayerController = other.Components.GetInAncestorsOrSelf<PlayerController>();

			if ( PlayerController.IsValid() )
				return PlayerController.GameObject;

			var Rigidbody = other.Components.GetInAncestorsOrSelf<Rigidbody>();

			if ( Rigidbody.IsValid() )
				return Rigidbody.GameObject;

			return other;
		}

	}



	/**
	 * TeleportDestinationVolume
	 */

	[Category( "Triggers" ), Icon( "adjust" )]
	public class TeleportDestinationVolume : Component
	{
		[RequireComponent]
		protected BoxCollider Volume { get; set; }

		protected override void OnAwake()
		{
			Volume ??= Components.GetOrCreate<BoxCollider>();
			Volume.IsTrigger = true;
		}

		// Gets random position destination inside the volume being aware of object bounds
		public Vector3 GetRandomPositionFor(GameObject Object)
		{
			BBox VolumeBounds = Volume.GetWorldBounds();
			BBox ObjectBounds = Object.GetBounds();

			Vector3 OriginToBoundsCenter = ObjectBounds.Center - Object.WorldPosition;
			Vector3 ObjectExtents = ObjectBounds.Extents;

			Vector3 Mins = VolumeBounds.Mins;
			Vector3 Maxs = VolumeBounds.Maxs;

			if(Mins.x > Maxs.x || Mins.y > Maxs.y || Mins.z > Maxs.z )
				return VolumeBounds.Center; // fallback to center if bounds are invalid

			BBox SafeBounds = new BBox( Mins, Maxs );
			Vector3 RandomLocationInVolume = SafeBounds.RandomPointInside;

			return RandomLocationInVolume - OriginToBoundsCenter;
		}
	}
}
