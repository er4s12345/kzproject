using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

/**
 * TriggerTeleport teleports objects that enter the trigger when they match the filter settings.
 * Death, damage, respawn, and run invalidation are owned by Deathrun damage components.
 */

namespace Sandbox.Components.Triggers
{
	[Category( "Triggers" ), Icon( "adjust" )]
	public class TriggerTeleport : TriggerBase, Component.ITriggerListener
	{
		[Property]
		[Description( "Zero-out object's velocity on teleport?" )]
		bool ResetVelocity { get; set; } = false;

		[Property, Required]
		private GameObject TeleportDestination { get; set; }

		[Property]
		[Description( "Teleport player/controller objects that enter the trigger." )]
		public bool TeleportPlayers { get; set; } = true;

		[Property]
		[Description( "Teleport physics objects that enter the trigger." )]
		public bool TeleportPhysics { get; set; } = true;

		[Property]
		[Description( "Log successful teleports and ignored trigger touches." )]
		public bool LogTeleport { get; set; } = false;

		private Vector3 _teleportDestinationLocation = Vector3.Zero;

		protected bool CanTeleport( GameObject target )
		{
			if ( !CanTrigger() || !target.IsValid() )
				return false;

			return MatchesTeleportFilter( target );
		}

		protected override void OnAwake()
		{
			TriggerCollider ??= Components.GetOrCreate<BoxCollider>();

			if ( TriggerCollider.IsValid() )
				TriggerCollider.IsTrigger = true;

			if ( !TeleportDestination.IsValid() )
			{
				Log.Error( $"TeleportDestination is not set for '{GameObject.Name}' - removing teleport trigger." );
				DestroyGameObject();
				return;
			}

			_teleportDestinationLocation = TeleportDestination.WorldPosition;
			GameObject.Tags.Add( "trigger" );
			Tags.Add( "trigger" );
		}

		protected override void DrawGizmos()
		{
			if ( !TriggerCollider.IsValid() || !TeleportDestination.IsValid() )
				return;

			base.DrawGizmos();

			var destinationLocal = WorldTransform.PointToLocal( TeleportDestination.WorldPosition );

			if ( Gizmo.IsSelected )
			{
				Gizmo.Draw.Color = Color.Red;
				Gizmo.Draw.LineThickness = 5;
				Gizmo.Draw.Line( TriggerCollider.Center, destinationLocal );
				Gizmo.Draw.LineThickness = 2.5f;
				Gizmo.Draw.LineBBox( new BBox( destinationLocal - Vector3.One * 10.0f, destinationLocal + Vector3.One * 10.0f ) );
			}
		}

		void ITriggerListener.OnTriggerEnter( GameObject other )
		{
			if ( Networking.IsActive && !Networking.IsHost )
				return;

			var target = ResolveTeleportTarget( other );

			if ( !target.IsValid() )
				return;

			if ( !CanTeleport( target ) )
			{
				if ( LogTeleport )
					Log.Info( $"TriggerTeleport '{GameObject.Name}' ignored '{target.Name}' because it did not match teleport filters." );

				return;
			}

			_teleportDestinationLocation = GetTeleportDestinationFor( target );

			TeleportTarget( target, _teleportDestinationLocation );

			LastTriggerTime = Time.Now;

			if ( LogTeleport )
				Log.Info( $"TriggerTeleport '{GameObject.Name}' moved '{target.Name}' to {_teleportDestinationLocation}." );

			if ( OnlyOnce )
				DestroyGameObject();
		}

		private Vector3 GetTeleportDestinationFor( GameObject target )
		{
			var destinationVolume = TeleportDestination.Components.Get<TeleportDestinationVolume>();

			if ( destinationVolume.IsValid() )
				return destinationVolume.GetRandomPositionFor( target );

			return TeleportDestination.WorldPosition;
		}

		private bool MatchesTeleportFilter( GameObject target )
		{
			if ( !target.IsValid() )
				return false;

			var isPlayer = target.Components.GetInAncestorsOrSelf<global::DeathrunPlayerController>().IsValid()
				|| target.Components.GetInAncestorsOrSelf<PlayerController>().IsValid();
			var isPhysics = target.Components.GetInAncestorsOrSelf<Rigidbody>().IsValid();

			if ( TagFilter.Any() && !target.Tags.HasAny( TagFilter ) )
				return false;

			if ( isPlayer )
				return TeleportPlayers;

			return TeleportPhysics && isPhysics;
		}

		private static GameObject ResolveTeleportTarget( GameObject other )
		{
			if ( !other.IsValid() )
				return null;

			var deathrunController = other.Components.GetInAncestorsOrSelf<global::DeathrunPlayerController>();

			if ( deathrunController.IsValid() )
				return deathrunController.GameObject;

			var playerController = other.Components.GetInAncestorsOrSelf<PlayerController>();

			if ( playerController.IsValid() )
				return playerController.GameObject;

			var rigidbody = other.Components.GetInAncestorsOrSelf<Rigidbody>();

			if ( rigidbody.IsValid() )
				return rigidbody.GameObject;

			return other;
		}

		private void TeleportTarget( GameObject target, Vector3 destination )
		{
			var deathrunController = target.Components.GetInAncestorsOrSelf<global::DeathrunPlayerController>();

			if ( deathrunController.IsValid() )
			{
				deathrunController.TeleportTo( destination, ResetVelocity, $"teleported by {GameObject.Name}" );
				return;
			}

			if ( ResetVelocity )
				ClearVelocity( target );

			target.WorldPosition = destination;

			if ( target.Network.Active )
				target.Network.ClearInterpolation();
		}

		private static void ClearVelocity( GameObject target )
		{
			var deathrunController = target.Components.GetInAncestorsOrSelf<global::DeathrunPlayerController>();

			if ( deathrunController.IsValid() )
			{
				deathrunController.ClearVelocity();
				deathrunController.ClearBaseVelocity();
				return;
			}

			var playerController = target.Components.GetInAncestorsOrSelf<PlayerController>();
			var rigidbody = playerController.IsValid() && playerController.Body.IsValid()
				? playerController.Body
				: target.Components.GetInAncestorsOrSelf<Rigidbody>();

			if ( rigidbody.IsValid() )
				rigidbody.Velocity = Vector3.Zero;
		}
	}

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

		public Vector3 GetRandomPositionFor( GameObject target )
		{
			var volumeBounds = Volume.GetWorldBounds();
			var targetBounds = target.GetBounds();
			var originToBoundsCenter = targetBounds.Center - target.WorldPosition;

			if ( volumeBounds.Mins.x > volumeBounds.Maxs.x || volumeBounds.Mins.y > volumeBounds.Maxs.y || volumeBounds.Mins.z > volumeBounds.Maxs.z )
				return volumeBounds.Center;

			return volumeBounds.RandomPointInside - originToBoundsCenter;
		}
	}
}
