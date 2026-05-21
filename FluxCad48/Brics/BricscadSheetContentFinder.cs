using System.Collections.Generic;

using Teigha.DatabaseServices;
using Bricscad.EditorInput;
using FluxCad48.Geometry;

namespace FluxCad48.Brics
{
	public static class BricscadSheetContentFinder
	{
		public static List<ObjectId> FindEntitiesInsideBounds(
			Transaction tr,
			Database db,
			Bounds2D frameBounds,
			Editor ed)
		{
			List<ObjectId> result = new List<ObjectId>();

			BlockTable bt =
				(BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(
					bt[BlockTableRecord.ModelSpace],
					OpenMode.ForRead);

			foreach (ObjectId id in modelSpace)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				Bounds2D entityBounds =
					BricscadEntityTools.GetEntityBounds(tr, id);

				if (entityBounds == null || !entityBounds.IsValid)
					continue;

				if (!TryDecideOwnership(ent, entityBounds, frameBounds, ed))
					continue;

				result.Add(id);

				BlockReference br = ent as BlockReference;

				if (br != null && frameBounds.Intersects(entityBounds))
				{
					result.Add(id);
					continue;
				}
			}

			return result;
		}

		private static bool TryDecideOwnership(
			Entity ent,
			Bounds2D entityBounds,
			Bounds2D frameBounds,
			Editor ed,
			double overlapThreshold = 0.60)
		{
			bool centerInside = frameBounds.ContainsPoint(
				entityBounds.CenterX,
				entityBounds.CenterY);

			double overlapRatio = entityBounds.IntersectionAreaRatio(frameBounds);

			bool keep = centerInside || overlapRatio >= overlapThreshold;

			ed.WriteMessage(
				$"\n[Ownership] Type={ent.GetType().Name}, CenterInside={centerInside}, Overlap={overlapRatio:0.000}, Keep={keep}");

			return keep;
		}

		private static bool IsOwnedByFrame(
			Bounds2D entityBounds,
			Bounds2D frameBounds,
			double overlapThreshold = 0.60)
		{
			if (entityBounds == null || !entityBounds.IsValid)
				return false;

			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			bool centerInside = frameBounds.ContainsPoint(
				entityBounds.CenterX,
				entityBounds.CenterY);

			if (centerInside)
				return true;

			double overlapRatio = entityBounds.IntersectionAreaRatio(frameBounds);

			return overlapRatio >= overlapThreshold;
		}

		public static List<ObjectId> FindOwnedEntitiesByFrameBounds(
			Database db,
			Transaction tr,
			Bounds2D frameBounds)
		{
			var result = new List<ObjectId>();

			var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
			var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

			foreach (ObjectId id in ms)
			{
				if (id.IsNull || id.IsErased)
					continue;

				var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (ent == null)
					continue;
				/*
				if (!TryGetBounds2D(ent, out var entityBounds))
					continue;

				if (IsOwnedByFrame(entityBounds, frameBounds, 0.60))
				{
					result.Add(id);
				}
				*/
			}

			return result;
		}
	}
}