using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Components.Triggers
{

	/**
	 * Base class for triggers. It contains common logic like drawing gizmos, definign required components
	 */
	public abstract class  TriggerBase : Component
	{
		[RequireComponent]
		protected BoxCollider TriggerCollider { get; set; }

		[Property]
		[Category( "Filter" )]
		[Description( "If set at least one tag, it will be used to filter out which objects can be teleported. If empty, no filter will be applied!" )]
		public TagSet TagFilter = new TagSet();

		[Property]
		[Category( "Filter" )]
		public bool TeleportPlayers = true;

		[Property]
		[Category( "Filter" )]
		public bool TeleportPhysics = true;

		[Property]
		[Description( "If true, the trigger will be triggered only once and then destroyed. Otherwise, it can be triggered multiple times with a delay defined by RetriggerDelay." )]
		protected bool OnlyOnce = false;

		[Property]
		[Description( "Delay in seconds before the trigger can be triggered again if OnlyOnce is false." )]
		protected virtual float RetriggerDelay { get; set; } = 0.5f;
		protected TimeSince LastTriggerTime = 999999999.0; // Set only after successful trigger, set to a very high value to allow triggering immediately after spawn


		protected bool CanTrigger() { return LastTriggerTime > RetriggerDelay; }

		protected override void DrawGizmos()
		{
			if ( !TriggerCollider.IsValid() )
				return;

			Vector3 TextPos = TriggerCollider.Center;
			Vector3 WorldTextPos = WorldTransform.PointToWorld( TextPos );
			float DistanceToCamera = Gizmo.CameraTransform.Position.Distance( WorldTextPos );

			bool DrawText = DistanceToCamera < 1000f;

			var halfSize = TriggerCollider.Scale * 0.5f;
			var box = new BBox( TriggerCollider.Center - halfSize, TriggerCollider.Center + halfSize );
			bool CanInteract = CanCursorReachBox( box );

			Gizmo.Draw.IgnoreDepth = Gizmo.IsSelected;
			Gizmo.Hitbox.CanInteract = CanInteract;
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.BBox( box );

			bool IsHovered = Gizmo.IsHovered && CanInteract;

			if ( Gizmo.HasClicked && Gizmo.Pressed.This && CanInteract )
				Gizmo.Select();
			
			var Color = IsHovered ? GizmoHoveredColor : GizmoColor;

			Gizmo.Draw.Color = Color.WithAlpha( 0.25f );
			Gizmo.Draw.SolidBox( box );

			Gizmo.Draw.Color = Color;
			Gizmo.Draw.LineBBox( box );

			if(DrawText)
			{
				Gizmo.Draw.IgnoreDepth = false;
				Gizmo.Draw.Color = GizmoTextColor;
				Gizmo.Draw.Text( GetType().Name, new Transform( TextPos ) );
			}
		}




		virtual protected Color GizmoColor => Color.Orange;
		virtual protected Color GizmoHoveredColor => Color.Yellow;
		virtual protected Color GizmoTextColor => Color.Green;

		private bool CanCursorReachBox( BBox localBox )
		{
			Ray worldRay = Gizmo.CurrentRay;
			Ray localRay = worldRay.ToLocal( WorldTransform );

			if ( !localBox.Trace( localRay, Gizmo.RayDepth, out float localBoxDistance ) )
				return false;

			Vector3 worldBoxHitPosition = WorldTransform.PointToWorld( localRay.Project( localBoxDistance ) );
			float worldBoxDistance = worldRay.Position.Distance( worldBoxHitPosition );

			var trace = Scene.Trace
				.Ray( worldRay, worldBoxDistance )
				.UseRenderMeshes( true )
				.UsePhysicsWorld( false )
				.WithoutTags( "hidden" )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();

			return !trace.Hit;
		}

	}
}
