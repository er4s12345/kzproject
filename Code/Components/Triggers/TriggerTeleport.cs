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
	public class TriggerTeleport : Component, Component.ITriggerListener
	{
		[RequireComponent]
		private BoxCollider TriggerCollider { get; set; }

		[RequireComponent]
		private ModelRenderer ModelRenderer { get; set; }

		[Property]
		bool TeleportPlayers { get; set; } = true;


		[Property]
		bool TeleportPhysics { get; set; } = true;

		// If set at least one tag, it will be used to filter out which objects can be teleported. If empty, no filter will be applied.
		[Property]
		[Description( "If set at least one tag, it will be used to filter out which objects can be teleported. If empty, no filter will be applied. NOT IMPLEMENTED YET!" )]
		TagSet TagFilter { get; set; } = new TagSet();

		[Property]
		[Description( "Zero-out object's velocity on teleport?" )]
		bool ResetVelocity { get; set; } = false;


		[Property, Required]
		private GameObject TeleportDestination { get; set; }

		private Vector3 _TeleportDestLocation = Vector3.Zero;


		protected bool CanTeleport(GameObject Object)
		{
			if ( Object == null ) return false;
			
			bool IsPlayer = Object.Components.Get<PlayerController>() != null;
			bool IsPhysics = Object.Components.Get<Rigidbody>() != null;

			if( TagFilter.Count() > 0)
			{
				// TODO: Add tag checking
			}

			return (TeleportPlayers && IsPlayer) || ( TeleportPhysics && IsPhysics );
		}

		protected override void OnAwake()
		{
			TriggerCollider ??= Components.GetOrCreate<BoxCollider>();
			ModelRenderer ??= Components.GetOrCreate<ModelRenderer>();

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

		protected override void OnStart()
		{
			if ( Game.IsPlaying )
				ModelRenderer.Enabled = false;
		}

		protected override void DrawGizmos()
		{
			if ( !TriggerCollider.IsValid() )
				return;


			var DestinationLocal = WorldTransform.PointToLocal( TeleportDestination.WorldPosition ); // NOTE: Gizmos are using local space for drawing, so we need to convert the world position to local position in this case

			var TextPos = TriggerCollider.Center;
			var DistanceToCamera = Gizmo.CameraTransform.Position.Distance( TextPos );
			bool DrawText = DistanceToCamera < 1000.0f;

			var halfSize = TriggerCollider.Scale * 0.5f;
			var box = new BBox(
				TriggerCollider.Center - halfSize,
				TriggerCollider.Center + halfSize
			);


			Gizmo.Draw.IgnoreDepth = Gizmo.IsSelected;
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.BBox( box );

			if ( Gizmo.HasClicked && Gizmo.Pressed.This )
			{
				Gizmo.Select();
			}

			var color = Gizmo.IsHovered ? Color.Yellow : Color.Orange;

			Gizmo.Draw.Color = color.WithAlpha( 0.25f );
			Gizmo.Draw.SolidBox( box );

			Gizmo.Draw.Color = color;
			Gizmo.Draw.LineBBox( box );

			if ( DrawText )
			{
				Gizmo.Draw.IgnoreDepth = false;
				Gizmo.Draw.Color = Color.Green;
				Gizmo.Draw.Text( "Trigger Teleport", new Transform( TextPos ) );
			}
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
			if ( other is not null && CanTeleport( other ) )
			{
				// Check if destination has destination volume component, if so, get random position inside the volume for teleportation.
				TeleportDestinationVolume DestinstaitonVolume = TeleportDestination.Components.Get<TeleportDestinationVolume>();
				if( DestinstaitonVolume != null )
					_TeleportDestLocation = DestinstaitonVolume.GetRandomPositionFor( other );
				

				if (ResetVelocity)
				{
					var rigidbody = other.Components.Get<Rigidbody>();
					if ( rigidbody.IsValid() )
						rigidbody.Velocity = Vector3.Zero;
				}

				other.WorldPosition = _TeleportDestLocation;
			}
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
